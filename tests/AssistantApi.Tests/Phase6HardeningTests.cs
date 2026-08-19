using System.Text;
using AssistantApi.Options;
using AssistantApi.Research;
using AssistantApi.Vk;

namespace AssistantApi.Tests;

/// <summary>
/// Phase 6 hardening: caps, ApiBaseUrl/MediaPath guards, non-goals
/// (no scrape/Apify/user-OAuth/RAG/MinIO/Direct).
/// </summary>
public sealed class Phase6HardeningTests
{
    [Fact]
    public void Vk_caps_are_explicit()
    {
        Assert.Equal(10, VkCommunityAllowlist.MaxCommunities);
        Assert.Equal(50, VkFetchLimits.MaxWallFetch);
        Assert.Equal(InstagramFetchLimits.MaxMediaFetch, VkFetchLimits.MaxWallFetch);
        Assert.Equal(25, VkFetchLimits.DefaultWallFetch);
        Assert.Equal(14, VkPhotoStoreLimits.MaxPhotos);
        Assert.Equal(ResearchImageLimits.MaxImages, VkPhotoStoreLimits.MaxPhotos);
        Assert.Equal(5, ResearchArtifactLimits.SnapshotCapPerUser);
        Assert.Equal(3, ResearchArtifactLimits.PlanCapPerUser);
        Assert.Equal(50, ResearchArtifactLimits.MaxPostsPerSnapshot);
        Assert.InRange(new VkOptions().MaxMediaDownloadBytes, 64 * 1024, 20 * 1024 * 1024);
    }

    [Fact]
    public void Api_base_url_only_api_vk_com()
    {
        Assert.True(VkApiHostGuard.IsAllowedApiBaseUrl("https://api.vk.com/method/", out _, out _));
        Assert.True(VkApiHostGuard.IsAllowedApiBaseUrl("https://api.vk.com/method", out _, out _));

        Assert.False(VkApiHostGuard.IsAllowedApiBaseUrl("https://m.vk.com/", out _, out var mReason));
        Assert.Equal("host-not-api-vk", mReason);

        Assert.False(VkApiHostGuard.IsAllowedApiBaseUrl("https://oauth.vk.com/authorize", out _, out var oauthReason));
        Assert.Equal("host-not-api-vk", oauthReason);

        Assert.False(VkApiHostGuard.IsAllowedApiBaseUrl("https://id.vk.com/oauth2", out _, out _));
        Assert.False(VkApiHostGuard.IsAllowedApiBaseUrl("http://api.vk.com/method/", out _, out var httpReason));
        Assert.Equal("scheme-not-https", httpReason);

        Assert.False(VkApiHostGuard.IsAllowedApiBaseUrl("https://127.0.0.1/method/", out _, out var ipReason));
        Assert.Equal("ip-literal-forbidden", ipReason);

        Assert.Equal(
            VkApiHostGuard.DefaultBaseUrl,
            VkApiHostGuard.NormalizeOrDefault("https://m.vk.com/wall"));
        Assert.StartsWith("https://api.vk.com/", VkApiHostGuard.NormalizeOrDefault(null), StringComparison.Ordinal);
    }

    [Fact]
    public void Media_path_rejects_cdn_and_absolute_urls()
    {
        Assert.True(ResearchMediaPathGuard.IsSafeRelativeMediaPath(
            "research-media/vk-abc/vk/photo-01.jpg", out _));

        Assert.False(ResearchMediaPathGuard.IsSafeRelativeMediaPath(
            "https://sun9-1.userapi.com/impg/x.jpg", out var cdnReason));
        Assert.Equal("absolute-url-forbidden", cdnReason);

        Assert.False(ResearchMediaPathGuard.IsSafeRelativeMediaPath(
            "../etc/passwd.jpg", out var travReason));
        Assert.Equal("path-traversal", travReason);

        Assert.Null(ResearchMediaPathGuard.SanitizeOrNull("https://userapi.com/a.jpg"));
        Assert.Equal(
            "research-media/vk-run/vk/photo-01.jpg",
            ResearchMediaPathGuard.SanitizeOrNull("research-media/vk-run/vk/photo-01.jpg"));
    }

    [Fact]
    public void Plan_serialize_strips_cdn_media_paths()
    {
        var plan = new ResearchPlan
        {
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            WindowStart = DateOnly.FromDateTime(DateTime.UtcNow),
            WindowEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(13)),
            Items =
            [
                new ResearchPlanItem
                {
                    Date = DateOnly.FromDateTime(DateTime.UtcNow),
                    Caption = "ok",
                    MediaPath = "https://sun9-1.userapi.com/evil.jpg",
                    Status = ResearchPlanItemStatus.Ready
                },
                new ResearchPlanItem
                {
                    Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                    Caption = "local",
                    MediaPath = "research-media/vk-x/vk/photo-01.jpg",
                    Status = ResearchPlanItemStatus.Ready
                }
            ]
        };

        var json = ResearchArtifactJson.SerializePlan(plan);
        Assert.DoesNotContain("userapi.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("research-media/vk-x/vk/photo-01.jpg", json, StringComparison.Ordinal);

        var roundTrip = ResearchArtifactJson.DeserializePlan(json);
        Assert.Null(roundTrip.Items[0].MediaPath);
        Assert.Equal("research-media/vk-x/vk/photo-01.jpg", roundTrip.Items[1].MediaPath);
    }

    [Fact]
    public void Apply_media_paths_skips_absolute_urls()
    {
        var plan = ResearchPlanBuilder.EmptyDraft("u1", "Ok");
        var updated = ResearchGeneratedImageCollector.ApplyMediaPaths(
            plan,
            ["https://sun9-1.userapi.com/a.jpg", "vk/photo-01.jpg"],
            "research-media/vk-run");

        Assert.Null(updated.Items[0].MediaPath);
        Assert.Equal("research-media/vk-run/vk/photo-01.jpg", updated.Items[1].MediaPath);
        Assert.True(Encoding.UTF8.GetByteCount(
            ResearchArtifactJson.SerializePlan(updated)) <= ResearchArtifactLimits.MaxPayloadBytes);
    }

    [Fact]
    public void Non_goals_not_wired_phase6()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var roots = new[]
        {
            Path.Combine(repoRoot, "src", "AssistantApi"),
            Path.Combine(repoRoot, "src", "TelegramGateway"),
            Path.Combine(repoRoot, "src", "CursorSdkBridge")
        };

        var wiringPatterns = new[]
        {
            "using Apify",
            "api.apify.com",
            "new Apify",
            "HtmlAgilityPack",
            "AngleSharp",
            "oauth.vk.com",
            "id.vk.com/oauth",
            "VKID",
            "VkIdOAuth",
            "IVkUserOAuth",
            "OpenAI.Images",
            "api.openai.com",
            "Elasticsearch.Net",
            "Elastic.Clients.Elasticsearch",
            "Nest.ElasticClient",
            "IEmbeddingGenerator",
            "OpenAIEmbedding",
            "Yandex.Direct",
            "m.vk.com/method"
        };

        var packagePatterns = new[]
        {
            "Apify",
            "HtmlAgilityPack",
            "AngleSharp",
            "OpenAI",
            "Azure.AI.OpenAI",
            "NEST",
            "Elastic.Clients.Elasticsearch",
            "Yandex.Direct"
        };

        var hits = new List<string>();

        foreach (var root in roots)
        {
            Assert.True(Directory.Exists(root), root);

            foreach (var csproj in Directory.EnumerateFiles(root, "*.csproj"))
            {
                var text = File.ReadAllText(csproj);
                foreach (var pkg in packagePatterns)
                {
                    if (text.Contains($"Include=\"{pkg}", StringComparison.OrdinalIgnoreCase)
                        || text.Contains($"Include=\"{pkg}.", StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add($"{Path.GetRelativePath(repoRoot, csproj)}: package {pkg}");
                    }
                }
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var (line, i) in File.ReadAllLines(file).Select((line, i) => (line, i + 1)))
                {
                    // Allow comments / docs that mention forbidden hosts as denylist text.
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // String literals in guards/tests that reject hosts are OK if surrounded by deny context.
                    if (line.Contains("host-not-api-vk", StringComparison.Ordinal)
                        || line.Contains("NormalizeOrDefault", StringComparison.Ordinal)
                        || line.Contains("IsAllowedApiBaseUrl", StringComparison.Ordinal)
                        || line.Contains("Replace(\"https://m.vk.com/", StringComparison.Ordinal)
                        || line.Contains("Replace(\"http://m.vk.com/", StringComparison.Ordinal)
                        || line.Contains("\"https://m.vk.com/", StringComparison.Ordinal)
                        || line.Contains("\"https://oauth.vk.com/", StringComparison.Ordinal)
                        || line.Contains("\"https://id.vk.com/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var pattern in wiringPatterns)
                    {
                        if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add($"{Path.GetRelativePath(repoRoot, file)}:{i}: {pattern}");
                        }
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    [Fact]
    public void Snapshot_vk_never_persists_cdn_urls()
    {
        var fetch = new VkWallFetchResult
        {
            Status = VkFetchStatus.Ok,
            OwnerId = -1,
            ScreenName = "pub",
            Posts =
            [
                new VkWallPost
                {
                    Id = 1,
                    OwnerId = -1,
                    Text = "hello https://sun9-1.userapi.com/impg/should-not-be-in-visual.jpg",
                    Photos =
                    [
                        new VkPhotoAttachment { Url = "https://sun9-1.userapi.com/impg/x.jpg", Width = 100, Height = 100 }
                    ]
                }
            ]
        };

        var snapshot = ResearchSnapshotBuilder.FromVkFetch("u", fetch);
        var json = ResearchArtifactJson.SerializeSnapshot(snapshot);
        // Caption may mention a URL typed by a user in post text — that is content, not a durable media ref.
        // VisualNotes / MediaPath / structured fields must not embed CDN as storage.
        Assert.DoesNotContain("\"mediaPath\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("PHOTO", snapshot.Posts[0].MediaType);
        Assert.DoesNotContain("userapi.com", snapshot.Posts[0].VisualNotes ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userapi.com", snapshot.Posts[0].Permalink ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
