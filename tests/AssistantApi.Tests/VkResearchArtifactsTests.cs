using AssistantApi.Harness;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Providers;
using AssistantApi.Rag;
using AssistantApi.Research;
using AssistantApi.Vk;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

public sealed class VkResearchArtifactsTests
{
    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "AssistantApi.Tests", "Fixtures", "vk", name);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            var outputCopy = Path.Combine(dir.FullName, "Fixtures", "vk", name);
            if (File.Exists(outputCopy))
            {
                return File.ReadAllText(outputCopy);
            }

            dir = dir.Parent;
        }

        throw new System.IO.FileNotFoundException($"VK fixture not found: {name}");
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
    public void Snapshot_builder_maps_vk_wall_with_source_vk_and_cap()
    {
        var posts = VkWallMapper.MapWallItems(Fixture("wall-get.json"), out var skippedDonut);
        Assert.Equal(1, skippedDonut); // donut soft-skipped in mapper
        Assert.Equal(2, posts.Count);

        var oversized = posts
            .Concat(Enumerable.Range(0, ResearchArtifactLimits.MaxPostsPerSnapshot + 5).Select(i => new VkWallPost
            {
                Id = 1000 + i,
                OwnerId = -1,
                Text = $"pad-{i}",
                Date = DateTimeOffset.UtcNow.AddMinutes(-i)
            }))
            .ToList();

        var fetch = new VkWallFetchResult
        {
            Status = VkFetchStatus.Ok,
            OwnerId = -123456789,
            ScreenName = "babor_bryansk",
            Posts = oversized,
            SkippedDonutCount = skippedDonut
        };

        var snapshot = ResearchSnapshotBuilder.FromVkFetch("u-vk", fetch);
        Assert.Equal(ResearchSources.Vk, snapshot.Source);
        Assert.Equal(ResearchArtifactLimits.MaxPostsPerSnapshot, snapshot.PostCount);
        Assert.Equal(ResearchArtifactLimits.MaxPostsPerSnapshot, snapshot.Posts.Count);
        Assert.Contains("source=vk", snapshot.Summary, StringComparison.Ordinal);
        Assert.Contains("babor_bryansk", snapshot.Summary, StringComparison.Ordinal);
        Assert.Contains("Осенний уход", snapshot.Posts[0].Caption, StringComparison.Ordinal);
        Assert.Equal("PHOTO", snapshot.Posts[0].MediaType);
        Assert.Contains("photos=1", snapshot.Posts[0].VisualNotes, StringComparison.Ordinal);

        var json = ResearchArtifactJson.SerializeSnapshot(snapshot);
        Assert.Contains("\"source\":\"vk\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("userapi.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VK__", json, StringComparison.OrdinalIgnoreCase);

        var roundTrip = ResearchArtifactJson.DeserializeSnapshot(json);
        Assert.Equal(ResearchSources.Vk, roundTrip.Source);
        Assert.Equal(ResearchArtifactLimits.MaxPostsPerSnapshot, roundTrip.Posts.Count);
    }

    [Fact]
    public async Task Capture_persists_snapshot_plan_episode_and_marketing_inject()
    {
        var posts = VkWallMapper.MapWallItems(Fixture("wall-get.json"), out var skipped);
        var vk = new FixedVkClient(new VkWallFetchResult
        {
            Status = VkFetchStatus.Ok,
            OwnerId = -123456789,
            ScreenName = "beauty_public",
            Posts = posts,
            SkippedDonutCount = skipped
        });

        var artifacts = new InMemoryResearchArtifactStore();
        var memory = new InMemoryHarnessMemoryStore();
        var capture = new VkResearchCapture(
            vk,
            artifacts,
            memory,
            new NoOpVkPhotoStore(),
            NullLogger<VkResearchCapture>.Instance);

        var result = await capture.CaptureAsync(
            "u-vk-cap",
            screenName: "beauty_public",
            conversationId: "c1",
            traceId: "t1");

        Assert.True(result.SnapshotSaved);
        Assert.True(result.PlanSaved);
        Assert.True(result.EpisodeSaved);
        Assert.Equal(VkFetchStatus.Ok, result.FetchStatus);
        Assert.Equal(ResearchSources.Vk, result.Snapshot!.Source);
        Assert.Equal(14, result.Plan!.Items.Count);
        Assert.Equal(1, result.SkippedDonutCount);

        var snap = await artifacts.GetLatestSnapshotAsync("u-vk-cap", CancellationToken.None);
        Assert.NotNull(snap);
        Assert.Equal(ResearchSources.Vk, snap!.Source);
        Assert.Equal(2, snap.PostCount);

        var injector = new ResearchPackInjector(artifacts);
        var marketing = await injector.BuildInjectBlockAsync("u-vk-cap", PackIds.Marketing, CancellationToken.None);
        var salon = await injector.BuildInjectBlockAsync("u-vk-cap", PackIds.Salon, CancellationToken.None);
        Assert.NotNull(marketing);
        Assert.Contains("source=vk", marketing, StringComparison.Ordinal);
        Assert.Contains("plan window=", marketing, StringComparison.Ordinal);
        Assert.Null(salon);

        var eps = await memory.GetRecentEpisodesAsync("u-vk-cap", PackIds.Marketing, 5, CancellationToken.None);
        Assert.Single(eps);
        Assert.Contains("VK research", eps[0].Task, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await memory.GetRecentEpisodesAsync("u-vk-cap", PackIds.Salon, 5, CancellationToken.None));

        // Cursor path: marketing prompt gets inject; salon does not
        var catalog = new PackCatalog(FindPacksRoot());
        var client = new RecordingClient();
        var provider = new CursorSdkLlmProvider(
            new FixedKeyStore("sk-test-key-not-real-xxxxxx"),
            new DomainHarness(catalog),
            client,
            catalog,
            new PackPromptBuilder(),
            new InMemoryAgentAffinityStore(),
            memory,
            injector,
            new NoOpRagPackInjector(),
            MsOptions.Create(new CursorOptions { Model = "composer-2.5" }),
            NullLogger<CursorSdkLlmProvider>.Instance);

        await provider.CompleteAsync(new Contracts.ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c-vk",
            UserId = "u-vk-cap",
            Text = "что в пабликах VK?",
            TraceId = "t2",
            Intent = Contracts.ChatIntent.Marketing
        }, CancellationToken.None);
        Assert.Contains("source=vk", client.LastPrompt, StringComparison.Ordinal);

        client.LastPrompt = "";
        await provider.CompleteAsync(new Contracts.ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c-vk",
            UserId = "u-vk-cap",
            Text = "удержание в салоне Babor",
            TraceId = "t3",
            Intent = Contracts.ChatIntent.Salon
        }, CancellationToken.None);
        Assert.DoesNotContain("source=vk", client.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Research artifacts", client.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capture_soft_fails_when_artifact_store_throws()
    {
        var vk = new FixedVkClient(new VkWallFetchResult
        {
            Status = VkFetchStatus.Ok,
            OwnerId = -1,
            Posts =
            [
                new VkWallPost { Id = 1, OwnerId = -1, Text = "ok", Date = DateTimeOffset.UtcNow }
            ]
        });
        var memory = new InMemoryHarnessMemoryStore();
        var capture = new VkResearchCapture(
            vk,
            new ThrowingArtifactStore(),
            memory,
            new NoOpVkPhotoStore(),
            NullLogger<VkResearchCapture>.Instance);

        var result = await capture.CaptureAsync("u-soft", ownerId: -1);
        Assert.False(result.SnapshotSaved);
        Assert.False(result.PlanSaved);
        Assert.True(result.EpisodeSaved);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(ResearchSources.Vk, result.Snapshot!.Source);
        Assert.NotNull(result.Plan);
    }

    [Fact]
    public async Task Capture_requires_target_and_skips_without_token()
    {
        var artifacts = new InMemoryResearchArtifactStore();
        var memory = new InMemoryHarnessMemoryStore();
        var capture = new VkResearchCapture(
            new StubVkWallClient(),
            artifacts,
            memory,
            new NoOpVkPhotoStore(),
            NullLogger<VkResearchCapture>.Instance);

        var missing = await capture.CaptureAsync("u1");
        Assert.Equal("vk-target-missing", missing.ErrorCode);
        Assert.False(missing.SnapshotSaved);

        var noToken = await capture.CaptureAsync("u1", screenName: "club1");
        Assert.Equal(VkFetchStatus.SkippedNoToken, noToken.FetchStatus);
        Assert.True(noToken.SnapshotSaved); // empty snapshot still soft-persisted
        Assert.Equal(ResearchSources.Vk, noToken.Snapshot!.Source);
        Assert.Equal(0, noToken.Snapshot.PostCount);
    }

    [Fact]
    public void Closed_wall_maps_to_empty_snapshot_source_vk()
    {
        Assert.True(VkWallMapper.TryMapApiError(
            System.Text.Json.JsonDocument.Parse(Fixture("wall-closed.json")).RootElement,
            out var status,
            out var code,
            out _));
        Assert.Equal(VkFetchStatus.SoftSkippedClosed, status);
        Assert.Equal("vk-wall-closed", code);

        var fetch = VkWallFetchResult.Soft(status, code, "closed");
        var snapshot = ResearchSnapshotBuilder.FromVkFetch("u", fetch);
        Assert.Equal(ResearchSources.Vk, snapshot.Source);
        Assert.Empty(snapshot.Posts);
        Assert.Contains("empty", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedVkClient : IVkWallClient
    {
        private readonly VkWallFetchResult _result;
        public FixedVkClient(VkWallFetchResult result) => _result = result;
        public bool IsConfigured => true;

        public Task<VkResolveResult> ResolveScreenNameAsync(
            string screenName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new VkResolveResult
            {
                Status = VkFetchStatus.Ok,
                ObjectType = "group",
                ObjectId = Math.Abs(_result.OwnerId ?? 1),
                OwnerId = _result.OwnerId
            });

        public Task<VkWallFetchResult> GetWallAsync(
            string? screenName = null,
            long? ownerId = null,
            int? count = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class FixedKeyStore : Security.ICursorApiKeyStore
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
}
