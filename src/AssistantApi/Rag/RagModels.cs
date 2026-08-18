namespace AssistantApi.Rag;

public sealed record RagHit(
    string DocumentId,
    string Domain,
    string? Title,
    string Snippet,
    double Score);

public static class RagDomains
{
    public const string Salon = "salon";
    public const string Marketing = "marketing";

    /// <summary>Pack id → rag domain, or null if pack must not retrieve (router/tasks/general).</summary>
    public static string? ForPack(string packId) => packId switch
    {
        Packs.PackIds.Salon => Salon,
        Packs.PackIds.Marketing => Marketing,
        _ => null
    };
}

public static class RagMcp
{
    /// <summary>Only MCP server name allowed on salon/marketing packs in this slice.</summary>
    public const string KbRetriever = "kb-retriever";
}
