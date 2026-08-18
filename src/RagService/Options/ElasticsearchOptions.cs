namespace RagService.Options;

public sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    /// <summary>
    /// Comma-separated URIs. Empty → in-memory index (tests / local without ES).
    /// Compose sets http://elasticsearch:9200 for rag-service only.
    /// </summary>
    public string Uris { get; set; } = string.Empty;

    public int RequestTimeoutSeconds { get; set; } = 10;

    public bool UseInMemory => string.IsNullOrWhiteSpace(Uris);
}
