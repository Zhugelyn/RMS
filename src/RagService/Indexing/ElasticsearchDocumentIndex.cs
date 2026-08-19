using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RagService.Contracts;
using RagService.Domain;
using RagService.Embedding;
using RagService.Options;

namespace RagService.Indexing;

/// <summary>
/// Elasticsearch REST client (no Nest). Owns kb-salon / kb-marketing only.
/// Soft-fail search → empty hits (must not cascade into /v1/chat later).
/// Basic auth when Elasticsearch:Username/Password set (phase7-hardening).
/// Logs never include query/text/snippet/password (PII-safe).
/// </summary>
public sealed class ElasticsearchDocumentIndex : IDocumentIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ILogger<ElasticsearchDocumentIndex> _logger;
    private readonly int _dims;
    private bool _indexesEnsured;

    public ElasticsearchDocumentIndex(
        HttpClient http,
        IOptions<ElasticsearchOptions> options,
        IEmbedder embedder,
        ILogger<ElasticsearchDocumentIndex> logger)
    {
        _http = http;
        _logger = logger;
        _dims = embedder.Dimensions;

        var opts = options.Value;
        var uri = opts.Uris.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Elasticsearch:Uris is required for ElasticsearchDocumentIndex.");

        _http.BaseAddress = new Uri(uri.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(opts.RequestTimeoutSeconds, 1, 60));

        if (opts.HasBasicAuth)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.Username}:{opts.Password}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    public string Mode => "elasticsearch";

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("_cluster/health?wait_for_status=yellow&timeout=5s", cancellationToken);
        response.EnsureSuccessStatusCode();
        await EnsureIndexesAsync(cancellationToken);
    }

    public async Task UpsertAsync(IndexedDocument document, CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken);

        var body = new
        {
            documentId = document.DocumentId,
            domain = document.Domain.ToString().ToLowerInvariant(),
            title = document.Title,
            text = document.Text,
            embedding = document.Embedding,
            metadata = document.Metadata
        };

        var path = $"{document.Index}/_doc/{Uri.EscapeDataString(document.DocumentId)}";
        using var response = await _http.PutAsJsonAsync(path, body, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Do not log response body (may echo document text). Surface truncated detail only in exception for caller.
            throw new InvalidOperationException(
                $"Elasticsearch upsert failed ({(int)response.StatusCode}) for index={document.Index} documentId={document.DocumentId}");
        }
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        RagDomain domain,
        string index,
        float[] queryEmbedding,
        string queryText,
        int topK,
        CancellationToken cancellationToken)
    {
        // Never query more than one index — domain isolation.
        if (index != RagIndexNames.For(domain))
        {
            throw new InvalidOperationException("Index/domain mismatch rejected.");
        }

        // Reject multi-index / wildcard paths.
        if (index.Contains(',', StringComparison.Ordinal)
            || index.Contains('*', StringComparison.Ordinal)
            || index.Contains('/', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Mixed or wildcard index paths rejected.");
        }

        try
        {
            await EnsureIndexesAsync(cancellationToken);

            var body = new
            {
                size = RagLimits.ClampTopK(topK),
                query = new
                {
                    script_score = new
                    {
                        query = new
                        {
                            @bool = new
                            {
                                should = new object[]
                                {
                                    new { match = new { text = queryText } },
                                    new { match = new { title = queryText } }
                                },
                                minimum_should_match = 0,
                                filter = new object[]
                                {
                                    new { term = new { domain = domain.ToString().ToLowerInvariant() } }
                                }
                            }
                        },
                        script = new
                        {
                            source = "cosineSimilarity(params.q, 'embedding') + 1.0",
                            @params = new { q = queryEmbedding }
                        }
                    }
                }
            };

            using var response = await _http.PostAsJsonAsync($"{index}/_search", body, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // PII-safe: status + index only — never ES body (may contain query/text).
                _logger.LogWarning(
                    "ES search soft-fail status={Status} index={Index}",
                    (int)response.StatusCode,
                    index);
                return Array.Empty<SearchHit>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("hits", out var hitsWrapper)
                || !hitsWrapper.TryGetProperty("hits", out var hitsArr))
            {
                return Array.Empty<SearchHit>();
            }

            var results = new List<SearchHit>();
            foreach (var hit in hitsArr.EnumerateArray())
            {
                if (!hit.TryGetProperty("_source", out var source))
                {
                    continue;
                }

                var text = source.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                var hitDomain = source.TryGetProperty("domain", out var d) ? d.GetString() : null;
                // Defense: drop cross-domain docs if ES filter ever fails.
                if (!string.IsNullOrEmpty(hitDomain)
                    && !string.Equals(hitDomain, domain.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(new SearchHit
                {
                    DocumentId = source.TryGetProperty("documentId", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Domain = domain.ToString().ToLowerInvariant(),
                    Title = source.TryGetProperty("title", out var title) ? title.GetString() : null,
                    Snippet = RagLimits.Snippet(text),
                    Score = hit.TryGetProperty("_score", out var score) && score.TryGetDouble(out var s) ? s : 0
                });
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ES search soft-fail for index={Index}", index);
            return Array.Empty<SearchHit>();
        }
    }

    private async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        if (_indexesEnsured)
        {
            return;
        }

        foreach (var index in new[] { RagIndexNames.Salon, RagIndexNames.Marketing })
        {
            using var head = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, index), cancellationToken);
            if (head.IsSuccessStatusCode)
            {
                continue;
            }

            var mapping = new
            {
                mappings = new
                {
                    properties = new
                    {
                        documentId = new { type = "keyword" },
                        domain = new { type = "keyword" },
                        title = new { type = "text" },
                        text = new { type = "text" },
                        embedding = new { type = "dense_vector", dims = _dims, index = false, similarity = "cosine" },
                        metadata = new { type = "object", enabled = false }
                    }
                }
            };

            using var put = await _http.PutAsJsonAsync(index, mapping, JsonOptions, cancellationToken);
            if (!put.IsSuccessStatusCode && put.StatusCode != System.Net.HttpStatusCode.BadRequest)
            {
                throw new InvalidOperationException(
                    $"Failed to create index {index} status={(int)put.StatusCode}");
            }
        }

        _indexesEnsured = true;
    }
}
