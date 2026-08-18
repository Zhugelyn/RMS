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

    /// <summary>Basic auth user (compose: elastic). Empty → no Authorization header.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Basic auth password (ELASTICSEARCH__PASSWORD). Never log.</summary>
    public string Password { get; set; } = string.Empty;

    public bool UseInMemory => string.IsNullOrWhiteSpace(Uris);

    public bool HasBasicAuth =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
