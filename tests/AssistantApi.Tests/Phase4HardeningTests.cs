using System.Text;
using AssistantApi.Options;
using AssistantApi.Research;

namespace AssistantApi.Tests;

/// <summary>Phase 4 hardening: limits, retention caps, non-goals (no Apify/OpenAI Images/RAG/ES).</summary>
public sealed class Phase4HardeningTests
{
    [Fact]
    public void Retention_caps_are_explicit()
    {
        Assert.Equal(5, ResearchArtifactLimits.SnapshotCapPerUser);
        Assert.Equal(3, ResearchArtifactLimits.PlanCapPerUser);
        Assert.Equal(14, ResearchArtifactLimits.PlanDays);
        Assert.Equal(14, ResearchImageLimits.MaxImages);
        Assert.Equal(50, InstagramFetchLimits.MaxMediaFetch);
        Assert.Equal(50, VkFetchLimits.MaxWallFetch);
        Assert.True(ResearchArtifactLimits.MaxPayloadBytes >= 64 * 1024);
        Assert.Equal(50, ResearchArtifactLimits.MaxPostsPerSnapshot);
    }

    [Fact]
    public void Snapshot_serialize_caps_posts_and_payload_size()
    {
        var posts = Enumerable.Range(0, ResearchArtifactLimits.MaxPostsPerSnapshot + 10)
            .Select(i => new ResearchSnapshotPost
            {
                MediaId = $"m{i}",
                Caption = "c",
                VisualNotes = "n"
            })
            .ToList();

        var json = ResearchArtifactJson.SerializeSnapshot(new ResearchSnapshot
        {
            UserId = "u1",
            CapturedAt = DateTimeOffset.UtcNow,
            Posts = posts,
            PostCount = posts.Count,
            Summary = "s"
        });

        var payload = ResearchArtifactJson.DeserializeSnapshot(json);
        Assert.Equal(ResearchArtifactLimits.MaxPostsPerSnapshot, payload.Posts.Count);
        Assert.True(Encoding.UTF8.GetByteCount(json) <= ResearchArtifactLimits.MaxPayloadBytes);
    }

    [Fact]
    public void Sanitize_run_id_blocks_path_segments()
    {
        var safe = ResearchGeneratedImageCollector.SanitizeRunId("../evil/../../x");
        Assert.DoesNotContain("..", safe, StringComparison.Ordinal);
        Assert.DoesNotContain('/', safe);
        Assert.DoesNotContain('\\', safe);
    }

    [Fact]
    public void Graph_fetch_hard_cap()
    {
        Assert.InRange(InstagramFetchLimits.DefaultMediaFetch, 1, InstagramFetchLimits.MaxMediaFetch);
        Assert.Equal(InstagramFetchLimits.MaxMediaFetch, Math.Clamp(999, 1, InstagramFetchLimits.MaxMediaFetch));
    }

    [Fact]
    public void Non_goals_not_wired_in_source()
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
            "OpenAI.Images",
            "Azure.AI.OpenAI",
            "api.openai.com",
            "Elasticsearch.Net",
            "Elastic.Clients.Elasticsearch",
            "Nest.ElasticClient",
            "IEmbeddingGenerator",
            "OpenAIEmbedding",
            "Yandex.Direct"
        };

        var packagePatterns = new[]
        {
            "Apify",
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
}
