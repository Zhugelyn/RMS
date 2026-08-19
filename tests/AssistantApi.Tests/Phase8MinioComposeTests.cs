namespace AssistantApi.Tests;

/// <summary>
/// phase8-minio-compose baseline + phase8-presign wiring: MinIO health/private buckets;
/// assistant-api wires MinIO env; gateway/bridge/rag do not.
/// </summary>
public sealed class Phase8MinioComposeTests
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
    public void Compose_defines_minio_with_healthcheck_and_internal_expose()
    {
        var yaml = ReadCompose();

        Assert.Contains("minio:", yaml, StringComparison.Ordinal);
        Assert.Contains("minio/minio:", yaml, StringComparison.Ordinal);
        Assert.Contains("minio-data:", yaml, StringComparison.Ordinal);
        Assert.Contains("/minio/health/live", yaml, StringComparison.Ordinal);

        var minioBlock = ExtractServiceBlock(yaml, "minio");
        Assert.Contains("expose:", minioBlock, StringComparison.Ordinal);
        Assert.Contains("\"9000\"", minioBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", minioBlock, StringComparison.Ordinal);
        Assert.Contains("healthcheck:", minioBlock, StringComparison.Ordinal);
        Assert.Contains("MINIO_ROOT_USER", minioBlock, StringComparison.Ordinal);
        Assert.Contains("MINIO_ROOT_PASSWORD", minioBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_minio_init_creates_private_salon_and_marketing_buckets()
    {
        var yaml = ReadCompose();

        Assert.Contains("minio-init:", yaml, StringComparison.Ordinal);
        var initBlock = ExtractServiceBlock(yaml, "minio-init");
        Assert.Contains("minio/mc:", initBlock, StringComparison.Ordinal);
        Assert.Contains("tg-ai-salon", initBlock, StringComparison.Ordinal);
        Assert.Contains("tg-ai-marketing", initBlock, StringComparison.Ordinal);
        Assert.Contains("anonymous set none", initBlock, StringComparison.Ordinal);
        Assert.Contains("mc mb", initBlock, StringComparison.Ordinal);
        Assert.Contains("depends_on:", initBlock, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", initBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_assistant_api_wires_minio_gateway_bridge_rag_do_not()
    {
        var yaml = ReadCompose();
        var api = ExtractServiceBlock(yaml, "assistant-api");
        Assert.Contains("Minio__Endpoint", api, StringComparison.Ordinal);
        Assert.Contains("http://minio:9000", api, StringComparison.Ordinal);
        Assert.Contains("minio:", api, StringComparison.Ordinal);
        Assert.Contains("minio-init:", api, StringComparison.Ordinal);

        foreach (var service in new[] { "telegram-gateway", "cursor-sdk-bridge", "rag-service" })
        {
            var block = ExtractServiceBlock(yaml, service);
            Assert.DoesNotContain("Minio__", block, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://minio", block, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("minio-data", block, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Compose_non_goals_no_direct_or_apify_or_public_ports_for_minio()
    {
        var yaml = ReadCompose();

        Assert.DoesNotContain("yandex-direct", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apify", yaml, StringComparison.OrdinalIgnoreCase);

        var minioBlock = ExtractServiceBlock(yaml, "minio");
        Assert.DoesNotContain("ports:", minioBlock, StringComparison.Ordinal);
    }

    /// <summary>Naive YAML service block extractor (indent-based), enough for compose assertions.</summary>
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
