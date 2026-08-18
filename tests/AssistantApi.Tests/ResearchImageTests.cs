using AssistantApi.Instagram;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Providers;
using AssistantApi.Research;
using AssistantApi.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

public sealed class ResearchImageTests
{
    [Fact]
    public void Collect_files_prefers_out_and_caps_at_14()
    {
        var root = Path.Combine(Path.GetTempPath(), "research-img-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "out"));
            Directory.CreateDirectory(Path.Combine(root, "refs"));
            File.WriteAllBytes(Path.Combine(root, "refs", "ref-01.jpg"), [1, 2, 3]);
            for (var i = 1; i <= 16; i++)
            {
                File.WriteAllBytes(Path.Combine(root, "out", $"day-{i:00}.png"), [1]);
            }

            File.WriteAllBytes(Path.Combine(root, "extra.webp"), [1]);

            var collected = ResearchGeneratedImageCollector.Collect(root, cap: 14);
            Assert.Equal(14, collected.Count);
            Assert.All(collected, p => Assert.StartsWith("out/", p, StringComparison.Ordinal));
            Assert.DoesNotContain(collected, p => p.StartsWith("refs/", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ApplyMediaPaths_persists_relative_paths_and_ready_status()
    {
        var plan = ResearchPlanBuilder.EmptyDraft("u1", InstagramFetchStatus.Ok, new DateOnly(2026, 8, 17));
        var images = Enumerable.Range(1, 3).Select(i => $"out/day-{i:00}.png").ToList();
        var updated = ResearchGeneratedImageCollector.ApplyMediaPaths(plan, images, "research-media/run1");
        Assert.Equal("research-media/run1/out/day-01.png", updated.Items[0].MediaPath);
        Assert.Equal(ResearchPlanItemStatus.Ready, updated.Items[0].Status);
        Assert.Null(updated.Items[3].MediaPath);
    }

    [Fact]
    public async Task Soft_fail_when_bridge_returns_image_tool_missing()
    {
        var artifacts = new InMemoryResearchArtifactStore();
        var bridge = new SoftFailBridge();
        var keys = new FixedKeyStore("cursor-test-key-not-real");
        var volume = Path.Combine(Path.GetTempPath(), "vol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(volume);

        var workspace = new ResearchImageWorkspace(
            MsOptions.Create(new ResearchOptions { ImageVolumePath = volume, ImageCap = 14 }),
            new RejectDownloader(),
            new FakeHostEnv(),
            NullLogger<ResearchImageWorkspace>.Instance);

        var gen = new ResearchImageGenerator(
            keys,
            bridge,
            workspace,
            artifacts,
            MsOptions.Create(new ResearchOptions { ImageVolumePath = volume, PackId = "marketing" }),
            MsOptions.Create(new CursorOptions { Model = "composer-2.5" }),
            NullLogger<ResearchImageGenerator>.Instance);

        var plan = ResearchPlanBuilder.EmptyDraft("u-img", InstagramFetchStatus.Ok);
        await artifacts.SavePlanAsync(plan, CancellationToken.None);

        var result = await gen.GenerateAsync(
            "u-img",
            "run-soft",
            plan,
            Array.Empty<InstagramMediaItem>(),
            CancellationToken.None);

        Assert.True(result.SoftFailed);
        Assert.Equal(ResearchImageLimits.SoftFailErrorCode, result.ErrorCode);
        Assert.Equal(0, result.ImageCount);
        Assert.True(bridge.Called);
        Assert.True(bridge.LastCollectImages);
    }

    [Fact]
    public async Task Skip_images_without_cursor_key_stack_alive()
    {
        var gen = new ResearchImageGenerator(
            new EmptyCursorApiKeyStore(),
            new SoftFailBridge(),
            new MissingWorkspace(),
            new InMemoryResearchArtifactStore(),
            MsOptions.Create(new ResearchOptions()),
            MsOptions.Create(new CursorOptions()),
            NullLogger<ResearchImageGenerator>.Instance);

        var plan = ResearchPlanBuilder.EmptyDraft("u-skip", InstagramFetchStatus.Ok);
        var result = await gen.GenerateAsync(
            "u-skip",
            "run",
            plan,
            Array.Empty<InstagramMediaItem>(),
            CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.Equal("cursor-key-missing", result.SkipReason);
        Assert.False(result.SoftFailed);
    }

    [Fact]
    public void No_openai_images_client_in_assistant_api()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AssistantApi"));
        Assert.True(Directory.Exists(root), root);
        var csproj = File.ReadAllText(Path.Combine(root, "AssistantApi.csproj"));
        Assert.DoesNotContain("OpenAI", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure.AI.OpenAI", csproj, StringComparison.OrdinalIgnoreCase);

        var hits = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x =>
                x.line.Contains("using OpenAI", StringComparison.Ordinal) ||
                x.line.Contains("OpenAI.Images", StringComparison.Ordinal) ||
                x.line.Contains("ImageClient", StringComparison.Ordinal) ||
                x.line.Contains("DallE", StringComparison.OrdinalIgnoreCase) ||
                x.line.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(hits.Count == 0, string.Join("\n", hits.Select(h => $"{h.f}:{h.i}: {h.line}")));
    }

    [Fact]
    public async Task Capture_sets_image_error_without_failing_plan()
    {
        var graph = new FakeOkGraph();
        var artifacts = new InMemoryResearchArtifactStore();
        var memory = new InMemoryHarnessMemoryStore();
        var images = new SoftFailImageGenerator();
        var capture = new InstagramResearchCapture(
            graph,
            artifacts,
            memory,
            images,
            NullLogger<InstagramResearchCapture>.Instance);

        var result = await capture.CaptureAsync("u-cap-img", traceId: "research-sched-test");
        Assert.True(result.PlanSaved);
        Assert.True(result.SnapshotSaved);
        Assert.Equal(ResearchImageLimits.SoftFailErrorCode, result.ImageErrorCode);
        Assert.Equal(0, result.ImageCount);
    }

    private sealed class SoftFailBridge : ICursorSdkClient
    {
        public bool Called { get; private set; }
        public bool LastCollectImages { get; private set; }

        public Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            LastCollectImages = request.CollectImages;
            return Task.FromResult(new CursorSdkRunResult(
                "soft-fail",
                string.Empty,
                request.PackId,
                Array.Empty<string>(),
                ResearchImageLimits.SoftFailErrorCode));
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

    private sealed class RejectDownloader : IInstagramMediaDownloader
    {
        public Task<InstagramMediaDownloadResult> TryDownloadAsync(string mediaUrl, CancellationToken cancellationToken) =>
            Task.FromResult(new InstagramMediaDownloadResult { Ok = false, ErrorCode = "skip" });
    }

    private sealed class FakeHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AssistantApi"));
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class MissingWorkspace : IResearchImageWorkspace
    {
        public bool IsConfigured => false;

        public Task<ResearchImageWorkspaceInfo?> PrepareAsync(
            string runId,
            ResearchPlan plan,
            IReadOnlyList<InstagramMediaItem> sourceMedia,
            CancellationToken cancellationToken) =>
            Task.FromResult<ResearchImageWorkspaceInfo?>(null);
    }

    private sealed class SoftFailImageGenerator : IResearchImageGenerator
    {
        public Task<ResearchImageGenerationResult> GenerateAsync(
            string userId,
            string runId,
            ResearchPlan plan,
            IReadOnlyList<InstagramMediaItem> sourceMedia,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ResearchImageGenerationResult
            {
                Attempted = true,
                SoftFailed = true,
                ErrorCode = ResearchImageLimits.SoftFailErrorCode,
                Plan = plan
            });
    }

    private sealed class FakeOkGraph : IInstagramGraphClient
    {
        public bool IsConfigured => true;

        public Task<InstagramMediaFetchResult> FetchOwnMediaAsync(
            int? limit = null,
            bool includeInsights = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstagramMediaFetchResult
            {
                Status = InstagramFetchStatus.Ok,
                Items =
                [
                    new InstagramMediaItem
                    {
                        Id = "m1",
                        Caption = "glow serum #beauty",
                        MediaType = "IMAGE"
                    }
                ]
            });
    }
}
