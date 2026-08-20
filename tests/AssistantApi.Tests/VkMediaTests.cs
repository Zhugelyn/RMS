using System.Net;
using System.Net.Http.Headers;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Research;
using AssistantApi.Vk;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

public sealed class VkMediaTests
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

    [Fact]
    public void CollectPhotoUrls_skips_non_allowlisted_and_caps()
    {
        var posts = new List<VkWallPost>
        {
            new()
            {
                Id = 1,
                OwnerId = -1,
                Photos =
                [
                    new VkPhotoAttachment { Url = "https://sun9-1.userapi.com/a.jpg" },
                    new VkPhotoAttachment { Url = "https://evil.example.com/x.jpg" }
                ]
            },
            new()
            {
                Id = 2,
                OwnerId = -1,
                Photos =
                [
                    new VkPhotoAttachment { Url = "https://sun9-2.userapi.com/b.jpg" }
                ]
            }
        };

        var urls = VkPhotoStore.CollectPhotoUrls(posts, max: 1);
        Assert.Single(urls);
        Assert.Contains("userapi.com", urls[0], StringComparison.Ordinal);
        Assert.DoesNotContain(urls, u => u.Contains("evil", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Downloader_rejects_non_allowlisted_url()
    {
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            }));
        var dl = new VkMediaDownloader(http, MsOptions.Create(new VkOptions()));
        var result = await dl.TryDownloadAsync("https://evil.example.com/x.jpg", CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("host-not-allowlisted", result.ErrorCode);
    }

    [Fact]
    public async Task Downloader_ok_for_userapi_jpeg()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var http = new HttpClient(new StubHandler(_ =>
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        var dl = new VkMediaDownloader(http, MsOptions.Create(new VkOptions()));
        var result = await dl.TryDownloadAsync("https://sun9-12.userapi.com/impg/ok.jpg", CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Equal(bytes, result.Bytes);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task Downloader_accepts_userapi_and_enforces_size()
    {
        var bytes = new byte[100];
        Array.Fill(bytes, (byte)7);
        var http = new HttpClient(new StubHandler(_ =>
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        var dl = new VkMediaDownloader(
            http,
            MsOptions.Create(new VkOptions { MaxMediaDownloadBytes = 50 }));
        var result = await dl.TryDownloadAsync("https://sun9-12.userapi.com/impg/ok.jpg", CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("content-too-large", result.ErrorCode);
    }

    [Fact]
    public async Task PhotoStore_skips_without_volume()
    {
        var store = new VkPhotoStore(
            MsOptions.Create(new ResearchOptions { ImageVolumePath = "" }),
            new RejectVkDownloader(),
            NullLogger<VkPhotoStore>.Instance);

        Assert.False(store.IsConfigured);
        var result = await store.DownloadWallPhotosAsync("run1", Array.Empty<VkWallPost>());
        Assert.True(result.Skipped);
        Assert.Equal("volume-missing", result.SkipReason);
    }

    [Fact]
    public async Task PhotoStore_writes_relative_paths_no_cdn_in_media_path()
    {
        var volume = Path.Combine(Path.GetTempPath(), "vk-vol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(volume);
        try
        {
            var payload = new byte[] { 0xFF, 0xD8, 0xFF, 0x01, 0x02, 0x03 };
            var store = new VkPhotoStore(
                MsOptions.Create(new ResearchOptions { ImageVolumePath = volume, MaxImageBytes = 1024 * 1024 }),
                new FixedVkDownloader(payload, "image/jpeg"),
                NullLogger<VkPhotoStore>.Instance);

            var posts = VkWallMapper.MapWallItems(Fixture("wall-get.json"), out _);
            var result = await store.DownloadWallPhotosAsync("test-run", posts);

            Assert.False(result.Skipped);
            Assert.Equal(1, result.Downloaded); // one photo post in fixture (donut skipped already)
            Assert.StartsWith("research-media/", result.RelativeRoot, StringComparison.Ordinal);
            Assert.All(result.MediaPaths, p =>
            {
                Assert.StartsWith("research-media/", p, StringComparison.Ordinal);
                Assert.Contains("/vk/photo-", p, StringComparison.Ordinal);
                Assert.DoesNotContain("userapi.com", p, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("http", p, StringComparison.OrdinalIgnoreCase);
            });

            var abs = Path.Combine(volume, result.MediaPaths[0].Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(abs));
            Assert.Equal(payload, await File.ReadAllBytesAsync(abs));
        }
        finally
        {
            try { Directory.Delete(volume, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Capture_downloads_photos_sets_plan_media_path_no_cdn_in_payload()
    {
        var volume = Path.Combine(Path.GetTempPath(), "vk-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(volume);
        try
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
            var store = new VkPhotoStore(
                MsOptions.Create(new ResearchOptions { ImageVolumePath = volume }),
                new FixedVkDownloader([1, 2, 3, 4], "image/jpeg"),
                NullLogger<VkPhotoStore>.Instance);

            var capture = new VkResearchCapture(
                vk,
                artifacts,
                memory,
                store,
                NullLogger<VkResearchCapture>.Instance);

            var result = await capture.CaptureAsync(
                "u-vk-media",
                screenName: "beauty_public",
                traceId: "tr1");

            Assert.True(result.SnapshotSaved);
            Assert.True(result.PlanSaved);
            Assert.Equal(1, result.PhotosDownloaded);
            Assert.NotNull(result.Plan!.Items[0].MediaPath);
            Assert.StartsWith("research-media/", result.Plan.Items[0].MediaPath, StringComparison.Ordinal);
            Assert.Equal(ResearchPlanItemStatus.Ready, result.Plan.Items[0].Status);

            var planJson = ResearchArtifactJson.SerializePlan(result.Plan);
            Assert.DoesNotContain("userapi.com", planJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", planJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("research-media/", planJson, StringComparison.Ordinal);

            var snapJson = ResearchArtifactJson.SerializeSnapshot(result.Snapshot!);
            Assert.DoesNotContain("userapi.com", snapJson, StringComparison.OrdinalIgnoreCase);

            var loaded = await artifacts.GetLatestPlanAsync("u-vk-media", CancellationToken.None);
            Assert.NotNull(loaded!.Items[0].MediaPath);
            Assert.DoesNotContain("userapi", loaded.Items[0].MediaPath!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(volume, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Capture_soft_skips_photos_without_volume_still_persists()
    {
        var posts = VkWallMapper.MapWallItems(Fixture("wall-get.json"), out var skipped);
        var vk = new FixedVkClient(new VkWallFetchResult
        {
            Status = VkFetchStatus.Ok,
            OwnerId = -1,
            ScreenName = "x",
            Posts = posts,
            SkippedDonutCount = skipped
        });
        var artifacts = new InMemoryResearchArtifactStore();
        var capture = new VkResearchCapture(
            vk,
            artifacts,
            new InMemoryHarnessMemoryStore(),
            new VkPhotoStore(
                MsOptions.Create(new ResearchOptions()),
                new RejectVkDownloader(),
                NullLogger<VkPhotoStore>.Instance),
            NullLogger<VkResearchCapture>.Instance);

        var result = await capture.CaptureAsync("u-novol", screenName: "beauty_public");
        Assert.True(result.SnapshotSaved);
        Assert.True(result.PlanSaved);
        Assert.Equal(0, result.PhotosDownloaded);
        Assert.Equal("volume-missing", result.PhotoSkipReason);
        Assert.Null(result.Plan!.Items[0].MediaPath);
    }

    [Fact]
    public void Cap_aligns_with_plan_gallery()
    {
        Assert.Equal(ResearchImageLimits.MaxImages, VkPhotoStoreLimits.MaxPhotos);
        Assert.Equal(14, VkPhotoStoreLimits.MaxPhotos);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_fn(request));
    }

    private sealed class FixedVkDownloader : IVkMediaDownloader
    {
        private readonly byte[] _bytes;
        private readonly string _ct;
        public FixedVkDownloader(byte[] bytes, string ct)
        {
            _bytes = bytes;
            _ct = ct;
        }

        public Task<VkMediaDownloadResult> TryDownloadAsync(string mediaUrl, CancellationToken cancellationToken)
        {
            if (!VkCdnUrlGuard.IsAllowedMediaUrl(mediaUrl, out _, out var reason))
            {
                return Task.FromResult(new VkMediaDownloadResult { Ok = false, ErrorCode = reason });
            }

            return Task.FromResult(new VkMediaDownloadResult
            {
                Ok = true,
                Bytes = _bytes,
                ContentType = _ct
            });
        }
    }

    private sealed class RejectVkDownloader : IVkMediaDownloader
    {
        public Task<VkMediaDownloadResult> TryDownloadAsync(string mediaUrl, CancellationToken cancellationToken) =>
            Task.FromResult(new VkMediaDownloadResult { Ok = false, ErrorCode = "reject" });
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
}
