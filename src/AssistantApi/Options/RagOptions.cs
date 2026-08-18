using System.ComponentModel.DataAnnotations;
using AssistantApi.Rag;

namespace AssistantApi.Options;

/// <summary>HTTP client to rag-service (ADR-014). Empty BaseUrl → NoOp retriever; soft-fail never breaks chat.</summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Internal rag-service base URL, e.g. http://rag-service:8080. Empty = disabled.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Inter-service key for X-Service-Key (≥16 when BaseUrl set). Never from chat.</summary>
    public string ServiceKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 10;

    [Range(1, RagClientLimits.MaxTopK)]
    public int TopK { get; set; } = RagClientLimits.DefaultTopK;

    [Range(RagClientLimits.MinInjectMaxChars, RagClientLimits.MaxInjectMaxChars)]
    public int InjectMaxChars { get; set; } = RagClientLimits.DefaultInjectMaxChars;
}
