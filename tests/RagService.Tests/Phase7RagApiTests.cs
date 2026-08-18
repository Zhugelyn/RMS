using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RagService.Contracts;

namespace RagService.Tests;

public sealed class Phase7RagApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ServiceKey = "test-rag-service-key!!";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly WebApplicationFactory<Program> _factory;

    public Phase7RagApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Rag:ServiceKey"] = ServiceKey,
                    // Empty URIs → in-memory index (no live ES required for unit tests).
                    ["Elasticsearch:Uris"] = ""
                });
            });
        });
    }

    [Fact]
    public async Task Health_endpoints_are_public()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Ingest_and_search_require_service_key()
    {
        var client = _factory.CreateClient();
        var ingest = await client.PostAsJsonAsync("/v1/ingest", new IngestRequest
        {
            DocumentId = "d1",
            Domain = RagDomain.Salon,
            Text = "babor salon bryansk"
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, ingest.StatusCode);

        var search = await client.PostAsJsonAsync("/v1/search", new SearchRequest
        {
            Domain = RagDomain.Salon,
            Query = "babor"
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, search.StatusCode);
    }

    [Fact]
    public async Task Ingest_then_search_same_domain_returns_hit()
    {
        var client = Authed();
        var ingest = await client.PostAsJsonAsync("/v1/ingest", new IngestRequest
        {
            DocumentId = "salon-doc-1",
            Domain = RagDomain.Salon,
            Title = "Babor Bryansk",
            Text = "Салон красоты Babor в Брянске: уход и локальный маркетинг."
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);

        var ingestBody = await ingest.Content.ReadFromJsonAsync<IngestResponse>(JsonOptions);
        Assert.NotNull(ingestBody);
        Assert.Equal(1, ingestBody!.SchemaVersion);
        Assert.Equal("kb-salon", ingestBody.Index);

        var search = await client.PostAsJsonAsync("/v1/search", new SearchRequest
        {
            Domain = RagDomain.Salon,
            Query = "Babor Брянск",
            TopK = 5
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);

        var body = await search.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(1, body!.SchemaVersion);
        Assert.Equal("kb-salon", body.Index);
        Assert.Contains(body.Hits, h => h.DocumentId == "salon-doc-1");
    }

    [Fact]
    public async Task Cross_domain_search_does_not_leak_salon_into_marketing()
    {
        var client = Authed();
        var unique = "unique-salon-phrase-" + Guid.NewGuid().ToString("N");

        var ingest = await client.PostAsJsonAsync("/v1/ingest", new IngestRequest
        {
            DocumentId = "iso-salon-1",
            Domain = RagDomain.Salon,
            Text = $"Secret salon only knowledge {unique}"
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);

        var marketingSearch = await client.PostAsJsonAsync("/v1/search", new SearchRequest
        {
            Domain = RagDomain.Marketing,
            Query = unique,
            TopK = 10
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, marketingSearch.StatusCode);

        var body = await marketingSearch.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("kb-marketing", body!.Index);
        Assert.DoesNotContain(body.Hits, h => h.DocumentId == "iso-salon-1");
        Assert.Empty(body.Hits);
    }

    [Fact]
    public async Task Marketing_ingest_not_visible_in_salon_search()
    {
        var client = Authed();
        var unique = "marketing-trend-" + Guid.NewGuid().ToString("N");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/ingest", new IngestRequest
        {
            DocumentId = "iso-mkt-1",
            Domain = RagDomain.Marketing,
            Text = $"Beauty market trend report {unique}"
        }, JsonOptions)).StatusCode);

        var salonSearch = await client.PostAsJsonAsync("/v1/search", new SearchRequest
        {
            Domain = RagDomain.Salon,
            Query = unique,
            TopK = 10
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, salonSearch.StatusCode);
        var body = await salonSearch.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("kb-salon", body!.Index);
        Assert.DoesNotContain(body.Hits, h => h.DocumentId == "iso-mkt-1");
    }

    [Fact]
    public void Index_names_only_salon_and_marketing()
    {
        Assert.Equal("kb-salon", RagService.Domain.RagIndexNames.For(RagDomain.Salon));
        Assert.Equal("kb-marketing", RagService.Domain.RagIndexNames.For(RagDomain.Marketing));
        Assert.True(RagService.Domain.RagIndexNames.TryParseDomain("salon", out var s) && s == RagDomain.Salon);
        Assert.False(RagService.Domain.RagIndexNames.TryParseDomain("tasks", out _));
        Assert.False(RagService.Domain.RagIndexNames.TryParseDomain("all", out _));
    }

    [Fact]
    public void Stub_embedder_is_deterministic_and_normalized()
    {
        var embedder = new RagService.Embedding.StubEmbedder();
        var a = embedder.Embed("hello world");
        var b = embedder.Embed("hello world");
        Assert.Equal(a, b);
        Assert.Equal(RagService.Embedding.StubEmbedder.DefaultDimensions, a.Length);
        var norm = Math.Sqrt(a.Sum(x => x * x));
        Assert.InRange(norm, 0.99, 1.01);
    }

    private HttpClient Authed()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ServiceKey);
        return client;
    }
}
