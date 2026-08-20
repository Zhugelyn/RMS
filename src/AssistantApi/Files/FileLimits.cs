namespace AssistantApi.Files;

public static class FileLimits
{
    public const long DefaultMaxUploadBytes = 20 * 1024 * 1024;
    public const long AbsoluteMaxUploadBytes = 100 * 1024 * 1024;
    public const int DefaultPresignTtlSeconds = 300;
    public const int MinPresignTtlSeconds = 30;
    public const int MaxPresignTtlSeconds = 3600;
    public const int DefaultPendingTtlSeconds = 3600;
    public const int MinPendingTtlSeconds = 60;
    public const int MaxPendingTtlSeconds = 86_400;
    public const int MaxOriginalFilenameLength = 200;
    public const int MaxContentTypeLength = 128;
    public const int DefaultPackListLimit = 8;
    public const int MaxPackListLimit = 20;
    public const int MaxPackInjectChars = 2400;
    public const int MaxFilenameInjectChars = 80;

    /// <summary>Short-TTL clamp for presigned PUT/GET (never longer than 1h).</summary>
    public static int ClampPresignTtlSeconds(int value) =>
        Math.Clamp(value, MinPresignTtlSeconds, MaxPresignTtlSeconds);

    /// <summary>Pending upload metadata TTL before confirm is rejected as expired.</summary>
    public static int ClampPendingTtlSeconds(int value) =>
        Math.Clamp(value, MinPendingTtlSeconds, MaxPendingTtlSeconds);

    /// <summary>MIME allowlist for upload intent (declared type). Scanner stub re-checks on confirm.</summary>
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
