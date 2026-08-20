using AssistantApi.Packs;

namespace AssistantApi.Files;

/// <summary>File domains with private MinIO buckets. Router/tasks have no files access.</summary>
public static class FileDomains
{
    public const string Salon = "salon";
    public const string Marketing = "marketing";

    public static bool IsAllowed(string? domain) =>
        string.Equals(domain, Salon, StringComparison.OrdinalIgnoreCase)
        || string.Equals(domain, Marketing, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string domain) =>
        domain.Trim().ToLowerInvariant() switch
        {
            Salon => Salon,
            Marketing => Marketing,
            _ => throw new FileValidationException("domain must be salon or marketing.")
        };

    /// <summary>Pack id → file domain, or null if pack must not see files (router/tasks).</summary>
    public static string? ForPack(string packId) => packId switch
    {
        PackIds.Salon => Salon,
        PackIds.Marketing => Marketing,
        _ => null
    };
}

public static class FileMcp
{
    /// <summary>Pack MCP name for MinIO files (salon|marketing only). Companion to kb-retriever.</summary>
    public const string Files = "files";
}
