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
}
