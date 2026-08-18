using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssistantApi.Data;
using AssistantApi.Memory;
using AssistantApi.Research;
using AssistantApi.Vk;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantApi.Tests;

public sealed class VkSettingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ServiceKey = "test-service-key-32chars-min!";

    private static WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Assistant:ServiceKey"] = ServiceKey
                });
            });
            if (configure is not null)
            {
                builder.ConfigureTestServices(configure);
            }
        });

    private static HttpClient Authed(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ServiceKey);
        return client;
    }

    [Theory]
    [InlineData("babor_bryansk", "babor_bryansk", null)]
    [InlineData("@Beauty_Public", "Beauty_Public", null)]
    [InlineData("club123", null, -123L)]
    [InlineData("public999", null, -999L)]
    [InlineData("-555", null, -555L)]
    [InlineData("https://vk.com/babor_bryansk", "babor_bryansk", null)]
    public void Allowlist_parse_one(string raw, string? screen, long? owner)
    {
        var t = VkCommunityAllowlist.ParseOne(raw);
        Assert.Equal(screen, t.ScreenName);
        Assert.Equal(owner, t.OwnerId);
    }

    [Fact]
    public void Allowlist_rejects_token_like_input()
    {
        Assert.Throws<ResearchValidationException>(() =>
            VkCommunityAllowlist.ParseOne("VK__SERVICETOKEN=abc"));
        Assert.Throws<ResearchValidationException>(() =>
            VkCommunityAllowlist.ParseOne("access_token=secret"));
    }

    [Fact]
    public void Allowlist_cap_and_dedupe()
    {
        var many = Enumerable.Range(1, 15)
            .Select(i => new VkCommunityTarget { ScreenName = $"club_{i}" })
            .Concat([new VkCommunityTarget { ScreenName = "club_1" }]);
        var normalized = VkCommunityAllowlist.Normalize(many);
        Assert.Equal(VkCommunityAllowlist.MaxCommunities, normalized.Count);
        Assert.Equal("club_1", normalized[0].ScreenName);
    }

    [Fact]
    public async Task Settings_put_get_vk_communities_additive()
    {
        await using var factory = CreateFactory();
        var client = Authed(factory);

        var put = await client.PutAsJsonAsync("/v1/research/settings", new
        {
            userId = "tg-42",
            enabled = true,
            instagramHandle = "@babor",
            vkCommunities = new object[]
            {
                new { screenName = "babor_bryansk" },
                new { ownerId = -123456L }
            },
            cadenceDays = 14,
            timezone = "Europe/Moscow"
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await put.Content.ReadFromJsonAsync<ResearchSettingsDto>(JsonOptions);
        Assert.Equal("babor", saved!.InstagramHandle);
        Assert.Equal(2, saved.VkCommunities.Count);
        Assert.Equal("babor_bryansk", saved.VkCommunities[0].ScreenName);
        Assert.Equal(-123456L, saved.VkCommunities[1].OwnerId);

        var get = await client.GetAsync("/v1/research/settings?userId=tg-42");
        var loaded = await get.Content.ReadFromJsonAsync<ResearchSettingsDto>(JsonOptions);
        Assert.Equal(2, loaded!.VkCommunities.Count);

        // Partial update without vkCommunities keeps allowlist
        var put2 = await client.PutAsJsonAsync("/v1/research/settings", new
        {
            userId = "tg-42",
            enabled = false
        });
        var kept = await put2.Content.ReadFromJsonAsync<ResearchSettingsDto>(JsonOptions);
        Assert.False(kept!.Enabled);
        Assert.Equal(2, kept.VkCommunities.Count);

        // Empty array clears
        var clear = await client.PutAsJsonAsync("/v1/research/settings", new
        {
            userId = "tg-42",
            vkCommunities = Array.Empty<object>()
        });
        var cleared = await clear.Content.ReadFromJsonAsync<ResearchSettingsDto>(JsonOptions);
        Assert.Empty(cleared!.VkCommunities);
    }

    [Fact]
    public async Task Settings_put_rejects_vk_token_in_community()
    {
        await using var factory = CreateFactory();
        var client = Authed(factory);

        var put = await client.PutAsJsonAsync("/v1/research/settings", new
        {
            userId = "tg-7",
            vkCommunities = new object[]
            {
                new { screenName = "VK__SERVICETOKEN" }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Run_source_vk_uses_allowlist_targets()
    {
        var vk = new RecordingVkClient();
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IVkWallClient>();
            services.AddSingleton<IVkWallClient>(vk);
        });
        var client = Authed(factory);

        await client.PutAsJsonAsync("/v1/research/settings", new
        {
            userId = "tg-55",
            vkCommunities = new object[]
            {
                new { screenName = "beauty_public" },
                new { ownerId = -999L }
            }
        });

        var emptyRun = await client.PostAsJsonAsync("/v1/research/run", new
        {
            userId = "tg-56",
            source = "vk"
        });
        var emptyBody = await emptyRun.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions);
        Assert.Equal(nameof(ResearchScheduleOutcome.NoOp), emptyBody!.Outcome);
        Assert.Equal("vk-allowlist-empty", emptyBody.ErrorCode);

        var run = await client.PostAsJsonAsync("/v1/research/run", new
        {
            userId = "tg-55",
            source = "vk",
            notifyChatId = "55"
        });
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var body = await run.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions);
        Assert.Equal(nameof(ResearchScheduleOutcome.Captured), body!.Outcome);
        Assert.Equal(2, vk.Calls.Count);
        Assert.Equal("beauty_public", vk.Calls[0].ScreenName);
        Assert.Equal(-999L, vk.Calls[1].OwnerId);

        var latest = await client.GetAsync("/v1/research/latest?userId=tg-55");
        var latestBody = await latest.Content.ReadFromJsonAsync<ResearchLatestResponse>(JsonOptions);
        Assert.NotNull(latestBody!.SnapshotSummary);
        Assert.Contains("source=vk", latestBody.SnapshotSummary!, StringComparison.Ordinal);
        Assert.Equal(2, latestBody.Settings.VkCommunities.Count);
    }

    [Fact]
    public async Task Capture_allowlist_merges_and_caps()
    {
        var postsA = Enumerable.Range(1, 30).Select(i => new VkWallPost
        {
            Id = i,
            OwnerId = -1,
            Text = $"a-{i}",
            Date = DateTimeOffset.UtcNow.AddMinutes(-i)
        }).ToList();
        var postsB = Enumerable.Range(1, 30).Select(i => new VkWallPost
        {
            Id = 100 + i,
            OwnerId = -2,
            Text = $"b-{i}",
            Date = DateTimeOffset.UtcNow.AddMinutes(-i - 30)
        }).ToList();

        var vk = new SequencedVkClient(
        [
            new VkWallFetchResult
            {
                Status = VkFetchStatus.Ok,
                ScreenName = "a",
                OwnerId = -1,
                Posts = postsA
            },
            new VkWallFetchResult
            {
                Status = VkFetchStatus.Ok,
                ScreenName = "b",
                OwnerId = -2,
                Posts = postsB
            }
        ]);

        var artifacts = new InMemoryResearchArtifactStore();
        var memory = new InMemoryHarnessMemoryStore();
        var capture = new VkResearchCapture(
            vk, artifacts, memory, new NoOpVkPhotoStore(), NullLogger<VkResearchCapture>.Instance);

        var result = await capture.CaptureAllowlistAsync(
            "u-merge",
            [
                new VkCommunityTarget { ScreenName = "pub_a" },
                new VkCommunityTarget { ScreenName = "pub_b" }
            ]);

        Assert.True(result.SnapshotSaved);
        Assert.Equal(2, result.TargetsOk);
        Assert.Equal(ResearchArtifactLimits.MaxPostsPerSnapshot, result.Snapshot!.PostCount);
        Assert.Equal(ResearchSources.Vk, result.Snapshot.Source);
    }

    [Fact]
    public async Task Postgres_settings_persist_vk_communities_json()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AssistantDbContext>()
            .UseSqlite(connection)
            .Options;
        var factory = new LocalDbFactory(options);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var store = new PostgresResearchSettingsStore(factory);
        await store.UpsertAsync(new ResearchSettings
        {
            UserId = "u-vk",
            InstagramHandle = "ig",
            VkCommunities =
            [
                new VkCommunityTarget { ScreenName = "babor_bryansk" },
                new VkCommunityTarget { OwnerId = -42 }
            ],
            Enabled = true,
            CadenceDays = 14
        }, CancellationToken.None);

        var loaded = await store.GetAsync("u-vk", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.VkCommunities.Count);
        Assert.Equal("babor_bryansk", loaded.VkCommunities[0].ScreenName);
        Assert.Equal(-42, loaded.VkCommunities[1].OwnerId);
    }

    private sealed class LocalDbFactory : IDbContextFactory<AssistantDbContext>
    {
        private readonly DbContextOptions<AssistantDbContext> _options;
        public LocalDbFactory(DbContextOptions<AssistantDbContext> options) => _options = options;
        public AssistantDbContext CreateDbContext() => new(_options);
    }

    private sealed class RecordingVkClient : IVkWallClient
    {
        public bool IsConfigured => true;
        public List<(string? ScreenName, long? OwnerId)> Calls { get; } = [];

        public Task<VkResolveResult> ResolveScreenNameAsync(string screenName, CancellationToken cancellationToken) =>
            Task.FromResult(new VkResolveResult
            {
                Status = VkFetchStatus.Ok,
                ObjectType = "group",
                ObjectId = 1,
                OwnerId = -1
            });

        public Task<VkWallFetchResult> GetWallAsync(
            string? screenName = null,
            long? ownerId = null,
            int? count = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((screenName, ownerId));
            return Task.FromResult(new VkWallFetchResult
            {
                Status = VkFetchStatus.Ok,
                ScreenName = screenName,
                OwnerId = ownerId ?? -1,
                Posts =
                [
                    new VkWallPost
                    {
                        Id = Calls.Count,
                        OwnerId = ownerId ?? -1,
                        Text = $"post from {screenName ?? ownerId.ToString()}",
                        Date = DateTimeOffset.UtcNow
                    }
                ]
            });
        }
    }

    private sealed class SequencedVkClient : IVkWallClient
    {
        private readonly Queue<VkWallFetchResult> _results;
        public bool IsConfigured => true;

        public SequencedVkClient(IEnumerable<VkWallFetchResult> results) =>
            _results = new Queue<VkWallFetchResult>(results);

        public Task<VkResolveResult> ResolveScreenNameAsync(string screenName, CancellationToken cancellationToken) =>
            Task.FromResult(new VkResolveResult { Status = VkFetchStatus.Ok, OwnerId = -1 });

        public Task<VkWallFetchResult> GetWallAsync(
            string? screenName = null,
            long? ownerId = null,
            int? count = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_results.Dequeue());
    }
}
