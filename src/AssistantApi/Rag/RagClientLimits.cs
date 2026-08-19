namespace AssistantApi.Rag;

/// <summary>Client-side caps for assistant-api → rag-service (align with RagService.Domain.RagLimits).</summary>
public static class RagClientLimits
{
    public const int DefaultTopK = 5;
    public const int MaxTopK = 20;
    public const int DefaultInjectMaxChars = 2400;
    public const int MaxInjectMaxChars = 8000;
    public const int MinInjectMaxChars = 200;
    public const int MaxTitleInjectChars = 80;
    public const int MaxSnippetInjectChars = 280;
    public const int MinServiceKeyLength = 16;

    public static int ClampTopK(int? topK, int configuredDefault = DefaultTopK) =>
        Math.Clamp(topK ?? configuredDefault, 1, MaxTopK);
}
