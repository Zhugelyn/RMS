using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RagService.Contracts;
using RagService.Domain;
using RagService.Security;

namespace RagService.Tests;

/// <summary>
/// phase7-hardening: caps, PII-safe logs, index isolation, ES basic auth compose,
/// non-goals (no MinIO/Direct/Apify; no mixing indexes).
/// </summary>
public sealed class Phase7HardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ServiceKey = "test-rag-service-key!!";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly WebApplicationFactory<Program> _factory;

    public Phase7HardeningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Rag:ServiceKey"] = ServiceKey,
                    ["Elasticsearch:Uris"] = ""
                });
            });
        });
    }

    [Fact]
    public void Caps_are_explicit()
    {
        Assert.Equal(128, RagLimits.MaxDocumentIdLength);
        Assert.Equal(512, RagLimits.MaxTitleLength);
        Assert.Equal(100_000, RagLimits.MaxTextLength);
        Assert.Equal(2_000, RagLimits.MaxQueryLength);
        Assert.Equal(5, RagLimits.DefaultTopK);
        Assert.Equal(20, RagLimits.MaxTopK);
        Assert.Equal(240, RagLimits.MaxSnippetChars);
        Assert.Equal(20, RagLimits.MaxMetadataEntries);
        Assert.Equal(16, RagLimits.MinServiceKeyLength);
        Assert.Equal(5, RagLimits.ClampTopK(0));
        Assert.Equal(20, RagLimits.ClampTopK(999));
        Assert.Equal(3, RagLimits.ClampTopK(3));
    }

    [Fact]
    public void Index_names_forbid_tasks_router_and_mixed()
    {
        Assert.Equal("kb-salon", RagIndexNames.For(RagDomain.Salon));
        Assert.Equal("kb-marketing", RagIndexNames.For(RagDomain.Marketing));
        Assert.False(RagIndexNames.TryParseDomain("tasks", out _));
        Assert.False(RagIndexNames.TryParseDomain("router", out _));
        Assert.False(RagIndexNames.TryParseDomain("_router", out _));
        Assert.False(RagIndexNames.TryParseDomain("salon,marketing", out _));
        Assert.False(RagIndexNames.TryParseDomain("kb-salon", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => RagIndexNames.For((RagDomain)99));
    }

    [Fact]
    public async Task Search_topK_above_max_is_rejected_by_validation()
    {
        var client = Authed();
        var response = await client.PostAsJsonAsync("/v1/search", new SearchRequest
        {
            Domain = RagDomain.Salon,
            Query = "babor",
            TopK = 99
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_secret_keys_are_stripped_on_ingest()
    {
        var client = Authed();
        var ingest = await client.PostAsJsonAsync("/v1/ingest", new IngestRequest
        {
            DocumentId = "meta-1",
            Domain = RagDomain.Salon,
            Text = "safe body about salon hours",
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "handbook",
                ["api_key"] = "should-drop",
                ["RAG__SERVICEKEY"] = "should-drop",
                ["password"] = "should-drop",
                ["token"] = "should-drop"
            }
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);

        // Round-trip search still works; secret metadata must not appear in response (response has no metadata field).
        var search = await client.PostAsJsonAsync("/v1/search", new SearchRequest
        {
            Domain = RagDomain.Salon,
            Query = "salon hours",
            TopK = 5
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var body = await search.Content.ReadAsStringAsync();
        Assert.DoesNotContain("should-drop", body, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cross_domain_isolation_both_directions()
    {
        var client = Authed();
        var salonPhrase = "salon-iso-" + Guid.NewGuid().ToString("N");
        var mktPhrase = "mkt-iso-" + Guid.NewGuid().ToString("N");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/ingest", new IngestRequest
        {
            DocumentId = "h-salon",
            Domain = RagDomain.Salon,
            Text = salonPhrase
        }, JsonOptions)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/ingest", new IngestRequest
        {
            DocumentId = "h-mkt",
            Domain = RagDomain.Marketing,
            Text = mktPhrase
        }, JsonOptions)).StatusCode);

        var mktSearch = await client.PostAsJsonAsync("/v1/search", new SearchRequest
        {
            Domain = RagDomain.Marketing,
            Query = salonPhrase,
            TopK = 10
        }, JsonOptions);
        var mktBody = await mktSearch.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);
        Assert.Equal("kb-marketing", mktBody!.Index);
        Assert.DoesNotContain(mktBody.Hits, h => h.DocumentId == "h-salon");

        var salonSearch = await client.PostAsJsonAsync("/v1/search", new SearchRequest
        {
            Domain = RagDomain.Salon,
            Query = mktPhrase,
            TopK = 10
        }, JsonOptions);
        var salonBody = await salonSearch.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);
        Assert.Equal("kb-salon", salonBody!.Index);
        Assert.DoesNotContain(salonBody.Hits, h => h.DocumentId == "h-mkt");
    }

    [Fact]
    public void Snippet_cap_applied()
    {
        var longText = new string('x', RagLimits.MaxSnippetChars + 50);
        var snip = RagLimits.Snippet(longText);
        Assert.True(snip.Length <= RagLimits.MaxSnippetChars + 1); // + ellipsis
        Assert.EndsWith("…", snip, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_logs_are_pii_safe()
    {
        var repoRoot = FindRepoRoot();
        var roots = new[]
        {
            Path.Combine(repoRoot, "src", "RagService"),
            Path.Combine(repoRoot, "src", "AssistantApi", "Rag")
        };

        var hits = new List<string>();
        foreach (var root in roots)
        {
            Assert.True(Directory.Exists(root), root);
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var (line, i) in File.ReadAllLines(file).Select((l, i) => (l, i + 1)))
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!trimmed.Contains("Log", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (RagPiiSafeLogging.LooksLikeForbiddenLogPayload(line))
                    {
                        hits.Add($"{Path.GetRelativePath(repoRoot, file)}:{i}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, "PII-unsafe log templates:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Compose_enables_es_basic_auth_and_wires_password_to_rag_service()
    {
        var yaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.yml"));
        var es = ExtractServiceBlock(yaml, "elasticsearch");
        Assert.Contains("xpack.security.enabled: \"true\"", es, StringComparison.Ordinal);
        Assert.Contains("ELASTIC_PASSWORD", es, StringComparison.Ordinal);
        Assert.Contains("-u elastic:", es, StringComparison.Ordinal);

        var rag = ExtractServiceBlock(yaml, "rag-service");
        Assert.Contains("Elasticsearch__Username", rag, StringComparison.Ordinal);
        Assert.Contains("Elasticsearch__Password", rag, StringComparison.Ordinal);
        Assert.Contains("ELASTICSEARCH__PASSWORD", rag, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_goals_not_wired_phase7()
    {
        var repoRoot = FindRepoRoot();
        var roots = new[]
        {
            Path.Combine(repoRoot, "src", "RagService"),
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
            "api.openai.com",
            "Yandex.Direct"
        };

        // assistant-api must not own ES clients; rag-service may use raw HTTP to ES.
        var assistantForbidden = new[]
        {
            "Elasticsearch.Net",
            "Elastic.Clients.Elasticsearch",
            "Nest.ElasticClient",
            "IElasticClient"
        };

        // Phase 8 opens MinIO on assistant-api only; rag/gateway/bridge still no object store.
        var packagePatterns = new[]
        {
            "Apify",
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

                // No Nest/Elastic client packages in assistant-api / gateway / bridge.
                if (!root.EndsWith("RagService", StringComparison.Ordinal))
                {
                    foreach (var pkg in new[] { "NEST", "Elastic.Clients.Elasticsearch", "Elasticsearch.Net" })
                    {
                        if (text.Contains($"Include=\"{pkg}", StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add($"{Path.GetRelativePath(repoRoot, csproj)}: package {pkg}");
                        }
                    }
                }

                // MinIO only on assistant-api (phase8-presign); rag/gateway/bridge stay clean.
                if (!root.EndsWith("AssistantApi", StringComparison.Ordinal)
                    && (text.Contains("Include=\"Minio\"", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("Include=\"AWSSDK.S3\"", StringComparison.OrdinalIgnoreCase)))
                {
                    hits.Add($"{Path.GetRelativePath(repoRoot, csproj)}: unexpected MinIO/S3 package");
                }
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var (line, i) in File.ReadAllLines(file).Select((l, i) => (l, i + 1)))
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal))
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

                    if (root.Contains("AssistantApi", StringComparison.Ordinal))
                    {
                        foreach (var pattern in assistantForbidden)
                        {
                            if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                hits.Add($"{Path.GetRelativePath(repoRoot, file)}:{i}: {pattern}");
                            }
                        }
                    }
                }
            }
        }

        var compose = File.ReadAllText(Path.Combine(repoRoot, "docker-compose.yml"));
        // MinIO container OK from phase8-minio-compose; still no Direct/Apify services.
        Assert.DoesNotContain("yandex-direct", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apify", compose, StringComparison.OrdinalIgnoreCase);

        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    private HttpClient Authed()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ServiceKey);
        return client;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
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
