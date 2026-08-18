using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Rag;

/// <summary>assistant-api → rag-service POST /v1/search with X-Service-Key. Soft-fail → empty hits.</summary>
public sealed class HttpRagRetriever : IRagRetriever
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _http;
    private readonly RagOptions _options;
    private readonly ILogger<HttpRagRetriever> _logger;

    public HttpRagRetriever(
        HttpClient http,
        IOptions<RagOptions> options,
        ILogger<HttpRagRetriever> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.ServiceKey);

    public async Task<IReadOnlyList<RagHit>> SearchAsync(
        string domain,
        string query,
        int? topK,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return Array.Empty<RagHit>();
        }

        var normalized = domain?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized is not (RagDomains.Salon or RagDomains.Marketing))
        {
            _logger.LogDebug("RAG search rejected unknown domain={Domain}", domain);
            return Array.Empty<RagHit>();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RagHit>();
        }

        var k = Math.Clamp(topK ?? _options.TopK, 1, 20);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/search");
            request.Headers.TryAddWithoutValidation("X-Service-Key", _options.ServiceKey);
            request.Content = JsonContent.Create(new SearchBody
            {
                Domain = normalized,
                Query = query.Trim(),
                TopK = k
            }, options: JsonOptions);

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "RAG search soft-fail status={Status} domain={Domain}",
                    (int)response.StatusCode,
                    normalized);
                return Array.Empty<RagHit>();
            }

            var body = await response.Content.ReadFromJsonAsync<SearchResponseBody>(JsonOptions, cancellationToken);
            if (body?.Hits is null || body.Hits.Count == 0)
            {
                return Array.Empty<RagHit>();
            }

            // Defense: never surface another domain's docs even if rag-service misbehaves.
            return body.Hits
                .Where(h => string.Equals(h.Domain, normalized, StringComparison.OrdinalIgnoreCase))
                .Select(h => new RagHit(
                    h.DocumentId ?? string.Empty,
                    normalized,
                    h.Title,
                    h.Snippet ?? string.Empty,
                    h.Score))
                .Where(h => !string.IsNullOrWhiteSpace(h.DocumentId) || !string.IsNullOrWhiteSpace(h.Snippet))
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG search soft-fail domain={Domain}", normalized);
            return Array.Empty<RagHit>();
        }
    }

    private sealed class SearchBody
    {
        public string Domain { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public int TopK { get; set; }
    }

    private sealed class SearchResponseBody
    {
        public int SchemaVersion { get; set; }
        public string? Domain { get; set; }
        public string? Index { get; set; }
        public List<SearchHitBody>? Hits { get; set; }
    }

    private sealed class SearchHitBody
    {
        public string? DocumentId { get; set; }
        public string? Domain { get; set; }
        public string? Title { get; set; }
        public string? Snippet { get; set; }
        public double Score { get; set; }
    }
}
