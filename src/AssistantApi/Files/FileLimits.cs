namespace AssistantApi.Files;

public static class FileLimits
{
    public const long DefaultMaxUploadBytes = 20 * 1024 * 1024;
    public const long AbsoluteMaxUploadBytes = 100 * 1024 * 1024;
    public const int DefaultPresignTtlSeconds = 300;
    public const int MaxPresignTtlSeconds = 3600;
    public const int MaxOriginalFilenameLength = 200;
    public const int MaxContentTypeLength = 128;

    /// <summary>MIME allowlist for upload intent (declared type). Detection/scanning → hardening.</summary>
    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "application/pdf",
        "text/plain",
        "video/mp4"
    };

    public static bool IsAllowedContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > MaxContentTypeLength)
        {
            return false;
        }

        // Strip parameters: "image/jpeg; charset=binary"
        var mime = contentType.Split(';', 2)[0].Trim();
        return AllowedContentTypes.Contains(mime);
    }

    public static string NormalizeContentType(string contentType) =>
        contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
}
