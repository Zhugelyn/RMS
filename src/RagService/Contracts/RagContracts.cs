using System.ComponentModel.DataAnnotations;

namespace RagService.Contracts;

public enum RagDomain
{
    Salon,
    Marketing
}

public sealed class IngestRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(128)]
    public string DocumentId { get; set; } = string.Empty;

    [Required]
    public RagDomain Domain { get; set; }

    [MaxLength(512)]
    public string? Title { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(100_000)]
    public string Text { get; set; } = string.Empty;

    /// <summary>Optional opaque metadata (no secrets). Capped by serializer size.</summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class IngestResponse
{
    public int SchemaVersion { get; set; } = 1;
    public string DocumentId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Index { get; set; } = string.Empty;
    public string Status { get; set; } = "upserted";
}

public sealed class SearchRequest
{
    [Required]
    public RagDomain Domain { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(2000)]
    public string Query { get; set; } = string.Empty;

    [Range(1, 20)]
    public int TopK { get; set; } = 5;
}

public sealed class SearchHit
{
    public string DocumentId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public double Score { get; set; }
}

public sealed class SearchResponse
{
    public int SchemaVersion { get; set; } = 1;
    public string Domain { get; set; } = string.Empty;
    public string Index { get; set; } = string.Empty;
    public IReadOnlyList<SearchHit> Hits { get; set; } = Array.Empty<SearchHit>();
}
