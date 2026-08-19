namespace RagService.Tests;

/// <summary>
/// phase7-rag-api compose contract: rag-service depends on healthy elasticsearch;
/// assistant-api still must not talk to ES directly.
/// </summary>
public sealed class Phase7RagComposeTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var compose = Path.Combine(dir.FullName, "docker-compose.yml");
            if (File.Exists(compose))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("docker-compose.yml not found from test base directory.");
    }

    private static string ReadCompose() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.yml"));

    [Fact]
    public void Compose_defines_rag_service_depending_on_healthy_elasticsearch()
    {
        var yaml = ReadCompose();
        Assert.Contains("rag-service:", yaml, StringComparison.Ordinal);

        var rag = ExtractServiceBlock(yaml, "rag-service");
        Assert.Contains("elasticsearch", rag, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("condition: service_healthy", rag, StringComparison.Ordinal);
        Assert.Contains("Elasticsearch__Uris", rag, StringComparison.Ordinal);
        Assert.Contains("Rag__ServiceKey", rag, StringComparison.Ordinal);
        Assert.Contains("healthcheck:", rag, StringComparison.Ordinal);
        Assert.Contains("expose:", rag, StringComparison.Ordinal);
        // Internal only — no host port publish for rag-service.
        Assert.DoesNotContain("ports:", rag, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_assistant_api_still_does_not_wire_elasticsearch()
    {
        var yaml = ReadCompose();
        var apiBlock = ExtractServiceBlock(yaml, "assistant-api");
        Assert.DoesNotContain("elasticsearch", apiBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ELASTICSEARCH", apiBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9200", apiBlock, StringComparison.Ordinal);
        // phase7-pack-retriever wires Rag__* HTTP client; this slice only asserts no ES.
        // Full Rag__BaseUrl assertions live in Phase7PackRetrieverTests.
    }

    [Fact]
    public void Compose_non_goals_no_direct_or_apify()
    {
        var yaml = ReadCompose();
        // MinIO allowed starting phase8-minio-compose; Direct/Apify still gated.
        Assert.DoesNotContain("yandex-direct", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apify", yaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assistant_api_source_has_no_elasticsearch_client()
    {
        var repoRoot = FindRepoRoot();
        var apiRoot = Path.Combine(repoRoot, "src", "AssistantApi");
        Assert.True(Directory.Exists(apiRoot), apiRoot);

        var forbidden = new[]
        {
            "Elasticsearch.Net",
            "Elastic.Clients.Elasticsearch",
            "Nest.ElasticClient",
            "IElasticClient",
            "ELASTICSEARCH__URIS",
            "ConnectionStrings__Elasticsearch"
        };

        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(apiRoot, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var pattern in forbidden)
            {
                if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add($"{Path.GetRelativePath(repoRoot, file)}: {pattern}");
                }
            }
        }

        Assert.True(hits.Count == 0, "Unexpected ES/app wiring:\n" + string.Join("\n", hits));
    }

    private static string ExtractServiceBlock(string yaml, string serviceName)
    {
        var marker = $"  {serviceName}:";
        var start = yaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"service '{serviceName}' not found");

        var from = start + marker.Length;
        var next = yaml.IndexOf("\n  ", from, StringComparison.Ordinal);
        while (next >= 0)
        {
            var lineStart = next + 1;
            var lineEnd = yaml.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = yaml.Length;
            }

            var line = yaml[lineStart..lineEnd];
            if (line.StartsWith("  ", StringComparison.Ordinal)
                && !line.StartsWith("   ", StringComparison.Ordinal)
                && line.TrimEnd().EndsWith(':')
                && !line.TrimStart().StartsWith('#'))
            {
                break;
            }

            next = yaml.IndexOf("\n  ", lineEnd, StringComparison.Ordinal);
        }

        var rootVolumes = yaml.IndexOf("\nvolumes:", from, StringComparison.Ordinal);
        var end = yaml.Length;
        if (next >= 0)
        {
            end = Math.Min(end, next);
        }

        if (rootVolumes >= 0)
        {
            end = Math.Min(end, rootVolumes);
        }

        return yaml[start..end];
    }
}
