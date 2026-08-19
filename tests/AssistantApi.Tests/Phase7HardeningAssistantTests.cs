using AssistantApi.Options;
using AssistantApi.Rag;

namespace AssistantApi.Tests;

/// <summary>
/// phase7-hardening (assistant-api side): client caps, pack domain guard,
/// non-goals (no MinIO/Direct/Apify; no ES client in assistant-api Rag module).
/// </summary>
public sealed class Phase7HardeningAssistantTests
{
    [Fact]
    public void Client_caps_align_with_rag_service()
    {
        Assert.Equal(5, RagClientLimits.DefaultTopK);
        Assert.Equal(20, RagClientLimits.MaxTopK);
        Assert.Equal(2400, RagClientLimits.DefaultInjectMaxChars);
        Assert.Equal(20, RagClientLimits.ClampTopK(999));
        Assert.Equal(5, RagClientLimits.ClampTopK(null));
        Assert.Equal(5, new RagOptions().TopK);
        Assert.Equal(2400, new RagOptions().InjectMaxChars);
    }

    [Fact]
    public void Router_and_tasks_still_have_no_rag_domain()
    {
        Assert.Null(RagDomains.ForPack("tasks"));
        Assert.Null(RagDomains.ForPack("_router"));
        Assert.Equal(RagDomains.Salon, RagDomains.ForPack("salon"));
        Assert.Equal(RagDomains.Marketing, RagDomains.ForPack("marketing"));
    }

    [Fact]
    public void Assistant_api_rag_module_has_no_es_client()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var ragDir = Path.Combine(repoRoot, "src", "AssistantApi", "Rag");
        Assert.True(Directory.Exists(ragDir), ragDir);

        var forbidden = new[]
        {
            "Elasticsearch.Net",
            "Elastic.Clients.Elasticsearch",
            "Nest.ElasticClient",
            "IElasticClient",
            "MinioClient",
            "Yandex.Direct",
            "api.apify.com"
        };

        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ragDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in forbidden)
            {
                if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add($"{Path.GetFileName(file)}: {pattern}");
                }
            }
        }

        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }
}
