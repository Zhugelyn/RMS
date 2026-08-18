namespace RagService.Domain;

/// <summary>Hard caps for Phase 7 RAG (phase7-hardening). Single source for ingest/search/snippet.</summary>
public static class RagLimits
{
    public const int MaxDocumentIdLength = 128;
    public const int MaxTitleLength = 512;
    public const int MaxTextLength = 100_000;
    public const int MaxQueryLength = 2_000;
    public const int DefaultTopK = 5;
    public const int MaxTopK = 20;
    public const int MaxSnippetChars = 240;
    public const int MaxMetadataEntries = 20;
    public const int MaxMetadataKeyLength = 64;
    public const int MaxMetadataValueLength = 512;
    public const int MinServiceKeyLength = 16;

    public static int ClampTopK(int topK) =>
        topK <= 0 ? DefaultTopK : Math.Clamp(topK, 1, MaxTopK);

    public static string Snippet(string text, int max = MaxSnippetChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text;
        }

        return text[..max] + "…";
    }
}
