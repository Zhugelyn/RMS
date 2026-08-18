using System.Net;
using System.Net.Http;
using AssistantApi.Contracts;
using AssistantApi.Data;
using AssistantApi.Harness;
using AssistantApi.Instagram;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Providers;
using AssistantApi.Research;
using AssistantApi.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

public sealed class ResearchArtifactsTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AssistantDbContext> _factory;

    public ResearchArtifactsTests()
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

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "AssistantApi.Tests", "Fixtures", "instagram", name);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            var outputCopy = Path.Combine(dir.FullName, "Fixtures", "instagram", name);
            if (File.Exists(outputCopy))
            {
                return File.ReadAllText(outputCopy);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Fixture not found: {name}");
    }

    private static string FindPacksRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AgentPacks");
            if (Directory.Exists(Path.Combine(candidate, "salon")))
            {
                return candidate;
            }

            var outputCopy = Path.Combine(dir.FullName, "AgentPacks");
            if (Directory.Exists(Path.Combine(outputCopy, "salon")))
            {
                return outputCopy;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("AgentPacks not found");
    }

    [Fact]
    public void Snapshot_builder_maps_fixture_media_without_token_fields()
    {
        var mapped = InstagramMediaMapper.MapMediaList(Fixture("media-list.json"));
        var items = new List<InstagramMediaItem>
        {
            new()
            {
                Id = mapped[0].Id,
                Caption = mapped[0].Caption + " #babor #bryansk",
                MediaUrl = mapped[0].MediaUrl,
                Timestamp = mapped[0].Timestamp,
                Permalink = mapped[0].Permalink,
                MediaType = mapped[0].MediaType,
                ThumbnailUrl = mapped[0].ThumbnailUrl,
                Insights = InstagramMediaMapper.MapInsights(Fixture("media-insights.json"))
            },
            mapped[1]
        };

        var fetch = new InstagramMediaFetchResult
        {
            Status = InstagramFetchStatus.Ok,
            Items = items,
            InsightsAttempted = true,
            InsightsAvailable = true
        };

        var snapshot = ResearchSnapshotBuilder.FromFetch("u-fix", fetch);
        Assert.Equal(2, snapshot.PostCount);
        Assert.Equal(2, snapshot.Posts.Count);
        Assert.Contains("Morning glow", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1200, snapshot.Posts[0].Impressions);
        Assert.DoesNotContain("access_token", ResearchArtifactJson.SerializeSnapshot(snapshot), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IGQVJ", ResearchArtifactJson.SerializeSnapshot(snapshot), StringComparison.OrdinalIgnoreCase);
        // Visual notes, not raw media URL dump for inject hygiene
        Assert.DoesNotContain("cdninstagram.com", snapshot.Posts[0].VisualNotes ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_builder_produces_exactly_14_items_with_required_fields()
    {
        var snapshot = new ResearchSnapshot
        {
            UserId = "u1",
            CapturedAt = DateTimeOffset.UtcNow,
            PostCount = 1,
            Summary = "posts=1; test caption",
            Posts =
            [
                new ResearchSnapshotPost
                {
                    MediaId = "m1",
                    Caption = "Glow serum #beauty #care",
                    MediaType = "IMAGE",
                    VisualNotes = "IMAGE reach=10",
                    Engagement = 5
                }
            ]
        };

        var start = new DateOnly(2026, 8, 17);
        var plan = ResearchPlanBuilder.FromSnapshot("u1", snapshot, start);
        Assert.Equal(14, plan.Items.Count);
        Assert.Equal(start, plan.WindowStart);
        Assert.Equal(start.AddDays(13), plan.WindowEnd);
        Assert.All(plan.Items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Caption));
            Assert.False(string.IsNullOrWhiteSpace(item.ImagePrompt));
            Assert.NotEmpty(item.Hashtags);
            Assert.Equal(ResearchPlanItemStatus.Draft, item.Status);
            Assert.Null(item.MediaPath);
            Assert.Null(item.TelegramFileId);
        });
        Assert.Contains(plan.Items[0].Hashtags, h => h.Equals("beauty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Postgres_store_persists_snapshot_and_plan_with_cap()
    {
        var store = new PostgresResearchArtifactStore(_factory);
        for (var i = 0; i < ResearchArtifactLimits.SnapshotCapPerUser + 2; i++)
        {
            await store.SaveSnapshotAsync(new ResearchSnapshot
            {
                UserId = "u-cap",
                CapturedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                PostCount = 0,
                Summary = $"snap-{i}",
                Posts = Array.Empty<ResearchSnapshotPost>(),
                SourceStatus = "Ok"
            }, CancellationToken.None);
        }

        var latest = await store.GetLatestSnapshotAsync("u-cap", CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal($"snap-{ResearchArtifactLimits.SnapshotCapPerUser + 1}", latest!.Summary);

        await using var db = await _factory.CreateDbContextAsync();
        var count = await db.ResearchSnapshots.CountAsync(x => x.UserId == "u-cap");
        Assert.Equal(ResearchArtifactLimits.SnapshotCapPerUser, count);

        var plan = ResearchPlanBuilder.FromSnapshot("u-cap", latest, new DateOnly(2026, 8, 17));
        var planId = await store.SavePlanAsync(plan, CancellationToken.None);
        Assert.True(planId > 0);
        var loaded = await store.GetLatestPlanAsync("u-cap", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(14, loaded!.Items.Count);
        Assert.Equal(plan.WindowStart, loaded.WindowStart);
    }

    [Fact]
    public async Task Capture_after_graph_fetch_persists_snapshot_plan_and_marketing_episode()
    {
        var mediaJson = Fixture("media-list.json");
        var insightsJson = Fixture("media-insights.json");
        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("/insights", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(insightsJson)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mediaJson)
            };
        });

        var graph = new HttpInstagramGraphClient(
            new HttpClient(handler),
            new FixedTokenStore("IGQVJ-test-token-not-real"),
            MsOptions.Create(new InstagramOptions
            {
                IgUserId = "17841400000000000",
                GraphBaseUrl = "https://graph.facebook.com/v21.0/"
            }),
            NullLogger<HttpInstagramGraphClient>.Instance);

        var artifacts = new InMemoryResearchArtifactStore();
        var memory = new InMemoryHarnessMemoryStore();
        var capture = new InstagramResearchCapture(
            graph,
            artifacts,
            memory,
            new NoOpResearchImageGenerator(),
            NullLogger<InstagramResearchCapture>.Instance);

        var result = await capture.CaptureAsync("u-research", "c1", "t1");
        Assert.True(result.SnapshotSaved);
        Assert.True(result.PlanSaved);
        Assert.True(result.EpisodeSaved);
        Assert.Equal(InstagramFetchStatus.Ok, result.FetchStatus);
        Assert.Equal(14, result.Plan!.Items.Count);

        var snap = await artifacts.GetLatestSnapshotAsync("u-research", CancellationToken.None);
        Assert.NotNull(snap);
        Assert.True(snap!.PostCount >= 1);

        var eps = await memory.GetRecentEpisodesAsync("u-research", PackIds.Marketing, 5, CancellationToken.None);
        Assert.Single(eps);
        Assert.Contains("Research", eps[0].Task, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("план", eps[0].Task, StringComparison.OrdinalIgnoreCase);

        var salonEps = await memory.GetRecentEpisodesAsync("u-research", PackIds.Salon, 5, CancellationToken.None);
        Assert.Empty(salonEps);
    }

    [Fact]
    public async Task Capture_soft_fails_when_artifact_store_throws_and_still_returns()
    {
        var graph = new FixedGraphClient(new InstagramMediaFetchResult
        {
            Status = InstagramFetchStatus.Ok,
            Items =
            [
                new InstagramMediaItem
                {
                    Id = "1",
                    Caption = "ok",
                    MediaType = "IMAGE"
                }
            ]
        });
        var memory = new InMemoryHarnessMemoryStore();
        var capture = new InstagramResearchCapture(
            graph,
            new ThrowingArtifactStore(),
            memory,
            new NoOpResearchImageGenerator(),
            NullLogger<InstagramResearchCapture>.Instance);

        var result = await capture.CaptureAsync("u-soft");
        Assert.False(result.SnapshotSaved);
        Assert.False(result.PlanSaved);
        Assert.True(result.EpisodeSaved); // episode store ok
        Assert.NotNull(result.Snapshot);
        Assert.NotNull(result.Plan);
    }

    [Fact]
    public async Task Marketing_prompt_injects_latest_snapshot_and_plan_salon_does_not()
    {
        var artifacts = new InMemoryResearchArtifactStore();
        var snapshot = new ResearchSnapshot
        {
            UserId = "u-inj",
            CapturedAt = DateTimeOffset.UtcNow,
            PostCount = 1,
            Summary = "UNIQUE_SNAP_MARKER glow serum trend",
            Posts =
            [
                new ResearchSnapshotPost { MediaId = "m1", Caption = "glow", MediaType = "IMAGE" }
            ],
            SourceStatus = "Ok"
        };
        await artifacts.SaveSnapshotAsync(snapshot, CancellationToken.None);
        await artifacts.SavePlanAsync(
            ResearchPlanBuilder.FromSnapshot("u-inj", snapshot, new DateOnly(2026, 8, 17)),
            CancellationToken.None);

        var injector = new ResearchPackInjector(artifacts);
        var marketingBlock = await injector.BuildInjectBlockAsync("u-inj", PackIds.Marketing, CancellationToken.None);
        var salonBlock = await injector.BuildInjectBlockAsync("u-inj", PackIds.Salon, CancellationToken.None);
        Assert.NotNull(marketingBlock);
        Assert.Contains("UNIQUE_SNAP_MARKER", marketingBlock, StringComparison.Ordinal);
        Assert.Contains("plan window=", marketingBlock, StringComparison.Ordinal);
        Assert.Null(salonBlock);

        var catalog = new PackCatalog(FindPacksRoot());
        var client = new RecordingClient();
        var provider = new CursorSdkLlmProvider(
            new FixedKeyStore("sk-test-key-not-real-xxxxxx"),
            new DomainHarness(catalog),
            client,
            catalog,
            new PackPromptBuilder(),
            new InMemoryAgentAffinityStore(),
            new InMemoryHarnessMemoryStore(),
            injector,
            MsOptions.Create(new CursorOptions { Model = "composer-2.5" }),
            NullLogger<CursorSdkLlmProvider>.Instance);

        await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c-inj",
            UserId = "u-inj",
            Text = "какие тренды в ленте?",
            TraceId = "t1",
            Intent = ChatIntent.Marketing
        }, CancellationToken.None);

        Assert.Contains("UNIQUE_SNAP_MARKER", client.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("Research artifacts", client.LastPrompt, StringComparison.Ordinal);

        client.LastPrompt = "";
        await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c-inj",
            UserId = "u-inj",
            Text = "что сделать для удержания в Babor?",
            TraceId = "t2",
            Intent = ChatIntent.Salon
        }, CancellationToken.None);

        Assert.DoesNotContain("UNIQUE_SNAP_MARKER", client.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Research artifacts", client.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_survives_when_research_inject_store_throws()
    {
        var catalog = new PackCatalog(FindPacksRoot());
        var client = new RecordingClient();
        var provider = new CursorSdkLlmProvider(
            new FixedKeyStore("sk-test-key-not-real-xxxxxx"),
            new DomainHarness(catalog),
            client,
            catalog,
            new PackPromptBuilder(),
            new InMemoryAgentAffinityStore(),
            new InMemoryHarnessMemoryStore(),
            new ResearchPackInjector(new ThrowingArtifactStore()),
            MsOptions.Create(new CursorOptions { Model = "composer-2.5" }),
            NullLogger<CursorSdkLlmProvider>.Instance);

        var response = await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c-fail",
            UserId = "u-fail",
            Text = "тренды брендов",
            TraceId = "t1",
            Intent = ChatIntent.Marketing
        }, CancellationToken.None);

        Assert.Equal("cursor-sdk", response.Provider);
        Assert.Equal(PackIds.Marketing, response.DomainPack);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }

    [Fact]
    public void Persistence_registers_in_memory_artifact_store_without_cs()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddAssistantPersistence(config);
        var sp = services.BuildServiceProvider();
        Assert.IsType<InMemoryResearchArtifactStore>(sp.GetRequiredService<IResearchArtifactStore>());
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AssistantDbContext>
    {
        private readonly DbContextOptions<AssistantDbContext> _options;
        public TestDbContextFactory(DbContextOptions<AssistantDbContext> options) => _options = options;
        public AssistantDbContext CreateDbContext() => new(_options);
        public ValueTask<AssistantDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }

    private sealed class FixedTokenStore : IInstagramTokenStore
    {
        private readonly string _token;
        public FixedTokenStore(string token) => _token = token;
        public bool HasToken => true;
        public bool TryGetAccessToken(out string accessToken)
        {
            accessToken = _token;
            return true;
        }
    }

    private sealed class FixedKeyStore : ICursorApiKeyStore
    {
        private readonly string _key;
        public FixedKeyStore(string key) => _key = key;
        public bool HasKey => true;
        public bool TryGetApiKey(out string apiKey)
        {
            apiKey = _key;
            return true;
        }
    }

    private sealed class FixedGraphClient : IInstagramGraphClient
    {
        private readonly InstagramMediaFetchResult _result;
        public FixedGraphClient(InstagramMediaFetchResult result) => _result = result;
        public bool IsConfigured => true;
        public Task<InstagramMediaFetchResult> FetchOwnMediaAsync(
            int? limit = null,
            bool includeInsights = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class ThrowingArtifactStore : IResearchArtifactStore
    {
        public Task<long> SaveSnapshotAsync(ResearchSnapshot snapshot, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom-snapshot");

        public Task<ResearchSnapshot?> GetLatestSnapshotAsync(string userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom-get-snapshot");

        public Task<long> SavePlanAsync(ResearchPlan plan, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom-plan");

        public Task<ResearchPlan?> GetLatestPlanAsync(string userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom-get-plan");
    }

    private sealed class RecordingClient : ICursorSdkClient
    {
        public string LastPrompt { get; set; } = "";
        private int _n;

        public Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken)
        {
            LastPrompt = request.Prompt;
            _n++;
            var pack = request.PackId ?? "none";
            var text = pack == PackIds.Salon
                ? "Ок, для Babor в Брянске предлагаю шаг по удержанию клиентов салона."
                : "По рынку красоты: гипотеза по аудитории и таргету без секретов.";
            return Task.FromResult(new CursorSdkRunResult($"agent-{pack}-{_n}", text, pack));
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
