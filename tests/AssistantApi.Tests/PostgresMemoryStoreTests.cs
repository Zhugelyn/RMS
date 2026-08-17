using AssistantApi.Data;
using AssistantApi.Memory;
using AssistantApi.Packs;
using AssistantApi.Research;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantApi.Tests;

public sealed class PostgresMemoryStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AssistantDbContext> _factory;

    public PostgresMemoryStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AssistantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestDbContextFactory(options);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Episodes_are_isolated_by_domain_marketing_does_not_leak_into_salon()
    {
        var store = new PostgresHarnessMemoryStore(_factory);

        await store.AddEpisodeAsync(new HarnessEpisode
        {
            UserId = "u1",
            Domain = PackIds.Salon,
            Task = "Babor: 3 мастера",
            Result = "зафиксировали штат",
            At = DateTimeOffset.UtcNow.AddMinutes(-2)
        }, CancellationToken.None);

        await store.AddEpisodeAsync(new HarnessEpisode
        {
            UserId = "u1",
            Domain = PackIds.Marketing,
            Task = "тренды брендов 25-34",
            Result = "гипотеза по аудитории",
            At = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, CancellationToken.None);

        var salon = await store.GetRecentEpisodesAsync("u1", PackIds.Salon, 10, CancellationToken.None);
        var marketing = await store.GetRecentEpisodesAsync("u1", PackIds.Marketing, 10, CancellationToken.None);

        Assert.Single(salon);
        Assert.Single(marketing);
        Assert.Contains("Babor", salon[0].Task, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Babor", marketing[0].Task, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("тренды брендов", salon[0].Task, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("тренды брендов", marketing[0].Task, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Research_settings_persist_with_default_cadence_14()
    {
        var store = new PostgresResearchSettingsStore(_factory);
        await store.UpsertAsync(new ResearchSettings
        {
            UserId = "u-research",
            InstagramHandle = "babor_bryansk",
            Enabled = true,
            CadenceDays = 0, // normalize → 14
            Timezone = "Europe/Moscow",
            NotifyChatId = "tg-1"
        }, CancellationToken.None);

        var loaded = await store.GetAsync("u-research", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(14, loaded!.CadenceDays);
        Assert.Equal("babor_bryansk", loaded.InstagramHandle);
        Assert.True(loaded.Enabled);
        Assert.Equal("Europe/Moscow", loaded.Timezone);
        Assert.Equal("tg-1", loaded.NotifyChatId);
        Assert.Null(loaded.LastError);
    }

    [Fact]
    public async Task ListDue_returns_only_enabled_with_nextRunAt_le_now()
    {
        var store = new PostgresResearchSettingsStore(_factory);
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        await store.UpsertAsync(new ResearchSettings
        {
            UserId = "due",
            Enabled = true,
            CadenceDays = 14,
            NextRunAt = now.AddMinutes(-5)
        }, CancellationToken.None);
        await store.UpsertAsync(new ResearchSettings
        {
            UserId = "future",
            Enabled = true,
            CadenceDays = 14,
            NextRunAt = now.AddDays(2)
        }, CancellationToken.None);
        await store.UpsertAsync(new ResearchSettings
        {
            UserId = "off",
            Enabled = false,
            CadenceDays = 14,
            NextRunAt = now.AddMinutes(-5)
        }, CancellationToken.None);

        var due = await store.ListDueAsync(now, CancellationToken.None);
        Assert.Single(due);
        Assert.Equal("due", due[0].UserId);
    }

    [Fact]
    public void Schema_contains_profiles_episodes_and_research_tables()
    {
        using var db = _factory.CreateDbContext();
        var entityTypes = db.Model.GetEntityTypes().Select(t => t.GetTableName()).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("user_profiles", entityTypes);
        Assert.Contains("harness_episodes", entityTypes);
        Assert.Contains("research_settings", entityTypes);
        Assert.Contains("research_snapshots", entityTypes);
        Assert.Contains("research_plans", entityTypes);
        Assert.Contains("research_schedule_runs", entityTypes);
    }

    [Fact]
    public void Persistence_falls_back_to_in_process_without_connection_string()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        Assert.False(PersistenceRegistration.HasPostgres(config));

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddAssistantPersistence(config);
        var sp = services.BuildServiceProvider();
        Assert.IsType<InMemoryHarnessMemoryStore>(sp.GetRequiredService<IHarnessMemoryStore>());
        Assert.IsType<InMemoryResearchSettingsStore>(sp.GetRequiredService<IResearchSettingsStore>());
        Assert.IsType<InMemoryResearchArtifactStore>(sp.GetRequiredService<IResearchArtifactStore>());
        Assert.IsType<InMemoryResearchScheduleRunStore>(sp.GetRequiredService<IResearchScheduleRunStore>());
    }

    [Fact]
    public void Persistence_resolves_ConnectionStrings_AssistantDb()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AssistantDb"] = "Host=postgres;Database=assistant;Username=assistant;Password=x"
            })
            .Build();
        Assert.True(PersistenceRegistration.HasPostgres(config));
        Assert.Contains("Host=postgres", PersistenceRegistration.ResolveConnectionString(config), StringComparison.Ordinal);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AssistantDbContext>
    {
        private readonly DbContextOptions<AssistantDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AssistantDbContext> options) => _options = options;

        public AssistantDbContext CreateDbContext() => new(_options);

        public ValueTask<AssistantDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}
