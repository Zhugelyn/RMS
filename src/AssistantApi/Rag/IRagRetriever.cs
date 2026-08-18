namespace AssistantApi.Rag;

/// <summary>
/// Pack-facing KB search via rag-service HTTP (ADR-014). Never talks to Elasticsearch directly.
/// Soft-fail: empty list on misconfig / HTTP / timeout — callers must not fail chat.
/// </summary>
public interface IRagRetriever
{
    /// <summary>True when Rag:BaseUrl is configured (HTTP path). False → NoOp.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Search domain index only. <paramref name="domain"/> must be salon|marketing.
    /// Cross-domain / unknown domain → empty hits (soft).
    /// </summary>
    Task<IReadOnlyList<RagHit>> SearchAsync(
        string domain,
        string query,
        int? topK,
        CancellationToken cancellationToken);
}
