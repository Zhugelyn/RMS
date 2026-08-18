namespace AssistantApi.Tests;

/// <summary>
/// phase7-es-compose: Elasticsearch in Docker Compose with healthcheck;
/// no app wiring (no rag-service, assistant-api does not depend on / connect to ES).
/// </summary>
public sealed class Phase7EsComposeTests
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

    private static string ReadCompose()
    {
        var path = Path.Combine(FindRepoRoot(), "docker-compose.yml");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Compose_defines_elasticsearch_with_healthcheck_and_internal_expose()
    {
        var yaml = ReadCompose();

        Assert.Contains("elasticsearch:", yaml, StringComparison.Ordinal);
        Assert.Contains("docker.elastic.co/elasticsearch/elasticsearch:", yaml, StringComparison.Ordinal);
        Assert.Contains("elasticsearch-data:", yaml, StringComparison.Ordinal);
        Assert.Contains("/_cluster/health", yaml, StringComparison.Ordinal);
        Assert.Contains("discovery.type: single-node", yaml, StringComparison.Ordinal);

        // Internal-only: expose 9200, do not publish host ports for ES.
        var esBlock = ExtractServiceBlock(yaml, "elasticsearch");
        Assert.Contains("expose:", esBlock, StringComparison.Ordinal);
        Assert.Contains("\"9200\"", esBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", esBlock, StringComparison.Ordinal);
        Assert.Contains("healthcheck:", esBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_does_not_wire_assistant_api_or_rag_service_to_elasticsearch()
    {
        var yaml = ReadCompose();

        Assert.DoesNotContain("rag-service:", yaml, StringComparison.Ordinal);

        var apiBlock = ExtractServiceBlock(yaml, "assistant-api");
        Assert.DoesNotContain("elasticsearch", apiBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ELASTICSEARCH", apiBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9200", apiBlock, StringComparison.Ordinal);

        var gatewayBlock = ExtractServiceBlock(yaml, "telegram-gateway");
        Assert.DoesNotContain("elasticsearch", gatewayBlock, StringComparison.OrdinalIgnoreCase);

        var bridgeBlock = ExtractServiceBlock(yaml, "cursor-sdk-bridge");
        Assert.DoesNotContain("elasticsearch", bridgeBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_non_goals_no_minio_or_direct_services()
    {
        var yaml = ReadCompose();

        Assert.DoesNotContain("minio:", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("yandex-direct", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apify", yaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assistant_api_source_has_no_elasticsearch_client_wiring()
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

    /// <summary>Naive YAML service block extractor (indent-based), enough for compose assertions.</summary>
    private static string ExtractServiceBlock(string yaml, string serviceName)
    {
        var marker = $"  {serviceName}:";
        var start = yaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"service '{serviceName}' not found");

        var from = start + marker.Length;
        var next = yaml.IndexOf("\n  ", from, StringComparison.Ordinal);
        // Skip nested "  " that are deeper than 2 spaces — look for next top-level service under services:
        while (next >= 0)
        {
            // next points at "\n  "; check if this is a sibling service (exactly two spaces then key then colon)
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
                // Could be another service or "volumes:" at root — volumes at root starts at column 0.
                break;
            }

            next = yaml.IndexOf("\n  ", lineEnd, StringComparison.Ordinal);
        }

        // Also stop at root-level keys (volumes:)
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
