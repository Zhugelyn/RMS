using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AssistantApi.Contracts;
using AssistantApi.Files;
using AssistantApi.Harness;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Providers;
using AssistantApi.Rag;
using AssistantApi.Research;
using AssistantApi.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

/// <summary>
/// phase7-pack-retriever: salon/marketing MCP kb-retriever + assistant-api → rag-service inject;
/// router/tasks without RAG; soft-fail empty hits; no MinIO/Direct/Apify/ES client.
/// </summary>
public sealed class Phase7PackRetrieverTests
{
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
    public void Compose_assistant_api_wires_rag_http_not_elasticsearch()
    {
        var yaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.yml"));
        var apiBlock = ExtractServiceBlock(yaml, "assistant-api");

        Assert.Contains("Rag__BaseUrl", apiBlock, StringComparison.Ordinal);
        Assert.Contains("Rag__ServiceKey", apiBlock, StringComparison.Ordinal);
        Assert.Contains("rag-service", apiBlock, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", apiBlock, StringComparison.Ordinal);

        Assert.DoesNotContain("elasticsearch", apiBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ELASTICSEARCH", apiBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9200", apiBlock, StringComparison.Ordinal);

        // phase8-presign wires MinIO into assistant-api; Direct/Apify still gated.
        Assert.Contains("Minio__Endpoint", apiBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("yandex-direct", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apify", yaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packs_salon_marketing_have_kb_retriever_router_tasks_do_not()
    {
        var catalog = new PackCatalog(FindPacksRoot());
        Assert.Equal(new[] { RagMcp.KbRetriever, FileMcp.Files }, catalog.GetRequired(PackIds.Salon).Mcp.Allowlist);
        Assert.Equal(new[] { RagMcp.KbRetriever, FileMcp.Files }, catalog.GetRequired(PackIds.Marketing).Mcp.Allowlist);
        Assert.Empty(catalog.GetRequired(PackIds.Tasks).Mcp.Allowlist);
        Assert.Empty(catalog.GetRequired(PackIds.Router).Mcp.Allowlist);
        Assert.Empty(catalog.GetRequired(PackIds.Tasks).Mcp.Servers);
        Assert.Empty(catalog.GetRequired(PackIds.Router).Mcp.Servers);
    }

    [Fact]
    public async Task Injector_salon_gets_hits_marketing_isolation_and_tasks_null()
    {
        var salonRetriever = new FakeRagRetriever();
        salonRetriever.Set(RagDomains.Salon, new RagHit("s1", RagDomains.Salon, "Babor FAQ", "UNIQUE_SALON_KB", 0.9));
        salonRetriever.Set(RagDomains.Marketing, new RagHit("m1", RagDomains.Marketing, "Market", "UNIQUE_MKT_KB", 0.8));

        var injector = new RagPackInjector(
            salonRetriever,
            MsOptions.Create(new RagOptions { TopK = 5, InjectMaxChars = 2400 }),
            NullLogger<RagPackInjector>.Instance);

        var salon = await injector.BuildInjectBlockAsync(PackIds.Salon, "услуги Babor", CancellationToken.None);
        Assert.NotNull(salon);
        Assert.Contains("UNIQUE_SALON_KB", salon, StringComparison.Ordinal);
        Assert.Contains(RagMcp.KbRetriever, salon, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE_MKT_KB", salon, StringComparison.Ordinal);

        var marketing = await injector.BuildInjectBlockAsync(PackIds.Marketing, "тренды", CancellationToken.None);
        Assert.NotNull(marketing);
        Assert.Contains("UNIQUE_MKT_KB", marketing, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE_SALON_KB", marketing, StringComparison.Ordinal);

        Assert.Null(await injector.BuildInjectBlockAsync(PackIds.Tasks, "напомни", CancellationToken.None));
        Assert.Null(await injector.BuildInjectBlockAsync(PackIds.Router, "route me", CancellationToken.None));
    }

    [Fact]
    public async Task Soft_fail_empty_and_throwing_retriever_do_not_break_chat()
    {
        var catalog = new PackCatalog(FindPacksRoot());
        var client = new RecordingClient();

        var emptyProvider = CreateProvider(catalog, client, new NoOpRagPackInjector());
        var empty = await emptyProvider.CompleteAsync(Sample(ChatIntent.Salon, "привет"), CancellationToken.None);
        Assert.Equal("cursor-sdk", empty.Provider);
        Assert.False(string.IsNullOrWhiteSpace(empty.Text));

        var throwing = CreateProvider(
            catalog,
            client,
            new RagPackInjector(
                new ThrowingRagRetriever(),
                MsOptions.Create(new RagOptions()),
                NullLogger<RagPackInjector>.Instance));
        var survived = await throwing.CompleteAsync(Sample(ChatIntent.Marketing, "тренды"), CancellationToken.None);
        Assert.Equal("cursor-sdk", survived.Provider);
        Assert.Equal(PackIds.Marketing, survived.DomainPack);
    }

    [Fact]
    public async Task Cursor_prompt_includes_rag_hits_for_salon_not_tasks()
    {
        var catalog = new PackCatalog(FindPacksRoot());
        var client = new RecordingClient();
        var fake = new FakeRagRetriever();
        fake.Set(RagDomains.Salon, new RagHit("s1", RagDomains.Salon, "T", "RAG_HIT_MARKER_XYZ", 1));

        var injector = new RagPackInjector(
            fake,
            MsOptions.Create(new RagOptions { TopK = 3, InjectMaxChars = 2000 }),
            NullLogger<RagPackInjector>.Instance);

        var provider = CreateProvider(catalog, client, injector);
        await provider.CompleteAsync(Sample(ChatIntent.Salon, "что известно о Babor?"), CancellationToken.None);
        Assert.Contains("RAG_HIT_MARKER_XYZ", client.LastPrompt, StringComparison.Ordinal);

        client.LastPrompt = "";
        await provider.CompleteAsync(Sample(ChatIntent.Tasks, "поставь напоминание"), CancellationToken.None);
        Assert.DoesNotContain("RAG_HIT_MARKER_XYZ", client.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Knowledge base hits", client.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_retriever_sends_service_key_and_domain_and_soft_fails_on_500()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"schemaVersion":1,"domain":"salon","index":"kb-salon","hits":[{"documentId":"d1","domain":"salon","title":"A","snippet":"snippet-a","score":0.5}]}""",
                Encoding.UTF8,
                "application/json")
        });
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://rag-service/") };
        var retriever = new HttpRagRetriever(
            http,
            MsOptions.Create(new RagOptions
            {
                BaseUrl = "http://rag-service",
                ServiceKey = "test-rag-service-key!!",
                TopK = 5,
                RequestTimeoutSeconds = 5
            }),
            NullLogger<HttpRagRetriever>.Instance);

        Assert.True(retriever.IsConfigured);
        var hits = await retriever.SearchAsync(RagDomains.Salon, "query", 3, CancellationToken.None);
        Assert.Single(hits);
        Assert.Equal("snippet-a", hits[0].Snippet);
        Assert.Equal("salon", handler.LastDomain);
        Assert.Equal("test-rag-service-key!!", handler.LastServiceKey);

        var soft = await retriever.SearchAsync(RagDomains.Salon, "query", 3, CancellationToken.None);
        Assert.Empty(soft);

        // Cross-domain filter: marketing hit in salon response is dropped.
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"schemaVersion":1,"domain":"salon","index":"kb-salon","hits":[{"documentId":"x","domain":"marketing","snippet":"leak","score":1}]}""",
                Encoding.UTF8,
                "application/json")
        });
        Assert.Empty(await retriever.SearchAsync(RagDomains.Salon, "q", 2, CancellationToken.None));
        Assert.Empty(await retriever.SearchAsync("tasks", "q", 2, CancellationToken.None));
    }

    [Fact]
    public void Assistant_api_has_no_direct_apify_or_es_client_packages()
    {
        var repoRoot = FindRepoRoot();
        var csproj = File.ReadAllText(Path.Combine(repoRoot, "src", "AssistantApi", "AssistantApi.csproj"));
        // MinIO allowed from phase8-presign; ES/Direct/Apify still forbidden on assistant-api.
        Assert.DoesNotContain("Elasticsearch", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NEST", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Apify", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yandex.Direct", csproj, StringComparison.OrdinalIgnoreCase);

        // No ES client types in Rag module (HTTP to rag-service only).
        var ragDir = Path.Combine(repoRoot, "src", "AssistantApi", "Rag");
        foreach (var file in Directory.EnumerateFiles(ragDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Elasticsearch.Net", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Elastic.Clients.Elasticsearch", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IElasticClient", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static CursorSdkLlmProvider CreateProvider(
        IPackCatalog catalog,
        RecordingClient client,
        IRagPackInjector ragInject) =>
        new(
            new FixedKeyStore("sk-test-key-not-real-xxxxxx"),
            new DomainHarness(catalog),
            client,
            catalog,
            new PackPromptBuilder(),
            new InMemoryAgentAffinityStore(),
            new InMemoryHarnessMemoryStore(),
            new ResearchPackInjector(new InMemoryResearchArtifactStore()),
            ragInject,
            new NoOpFilesPackInjector(),
            MsOptions.Create(new CursorOptions { Model = "composer-2.5" }),
            NullLogger<CursorSdkLlmProvider>.Instance);

    private static ChatRequest Sample(ChatIntent intent, string text) => new()
    {
        SchemaVersion = 1,
        ConversationId = "c-rag",
        UserId = "u-rag",
        Text = text,
        TraceId = "t-rag",
        Intent = intent
    };

    private sealed class FakeRagRetriever : IRagRetriever
    {
        private readonly Dictionary<string, List<RagHit>> _byDomain = new(StringComparer.OrdinalIgnoreCase);
        public bool IsConfigured => true;

        public void Set(string domain, params RagHit[] hits) =>
            _byDomain[domain] = hits.ToList();

        public Task<IReadOnlyList<RagHit>> SearchAsync(
            string domain, string query, int? topK, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_byDomain.TryGetValue(domain, out var list))
            {
                return Task.FromResult<IReadOnlyList<RagHit>>(Array.Empty<RagHit>());
            }

            return Task.FromResult<IReadOnlyList<RagHit>>(list);
        }
    }

    private sealed class ThrowingRagRetriever : IRagRetriever
    {
        public bool IsConfigured => true;

        public Task<IReadOnlyList<RagHit>> SearchAsync(
            string domain, string query, int? topK, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated rag outage");
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

    private sealed class RecordingClient : ICursorSdkClient
    {
        public string LastPrompt { get; set; } = "";
        private int _n;

        public Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken)
        {
            LastPrompt = request.Prompt;
            _n++;
            var pack = request.PackId ?? "salon";
            return Task.FromResult(new CursorSdkRunResult(
                agentId: $"agent-{pack}-{_n}",
                text: pack switch
                {
                    "marketing" => "marketing specialist reply about beauty market trends",
                    "tasks" => "tasks specialist reply about schedule reminder",
                    _ => "salon specialist reply about Babor Bryansk growth"
                }));
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public string? LastServiceKey { get; private set; }
        public string? LastDomain { get; private set; }

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("X-Service-Key", out var keys))
            {
                LastServiceKey = keys.FirstOrDefault();
            }

            if (request.Content is not null)
            {
                var json = await request.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("domain", out var d))
                {
                    LastDomain = d.GetString();
                }
            }

            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
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
