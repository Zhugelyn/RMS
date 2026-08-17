namespace AssistantApi.Instagram;

public enum InstagramFetchStatus
{
    Ok,
    /// <summary>No token configured — honest skip/stub path.</summary>
    SkippedNoToken,
    /// <summary>Token present but account id missing.</summary>
    SkippedNoAccountId,
    RateLimited,
    TokenExpired,
    SoftError
}

public sealed class InstagramMediaItem
{
    public string Id { get; init; } = string.Empty;
    public string? Caption { get; init; }
    public string? MediaUrl { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public string? Permalink { get; init; }
    public string? MediaType { get; init; }
    public string? ThumbnailUrl { get; init; }
    public InstagramMediaInsights? Insights { get; init; }
}

public sealed class InstagramMediaInsights
{
    public long? Impressions { get; init; }
    public long? Reach { get; init; }
    public long? Engagement { get; init; }
    public long? Saved { get; init; }
    public long? VideoViews { get; init; }
    public IReadOnlyDictionary<string, long> Raw { get; init; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
}

public sealed class InstagramMediaFetchResult
{
    public InstagramFetchStatus Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<InstagramMediaItem> Items { get; init; } = Array.Empty<InstagramMediaItem>();
    public bool InsightsAttempted { get; init; }
    public bool InsightsAvailable { get; init; }

    public static InstagramMediaFetchResult SkipNoToken() => new()
    {
        Status = InstagramFetchStatus.SkippedNoToken,
        ErrorCode = "instagram-token-missing",
        Message = "Instagram Graph token is not configured; feed fetch skipped."
    };

    public static InstagramMediaFetchResult SkipNoAccountId() => new()
    {
        Status = InstagramFetchStatus.SkippedNoAccountId,
        ErrorCode = "instagram-account-missing",
        Message = "Instagram IgUserId / BusinessAccountId is not configured; feed fetch skipped."
    };

    public static InstagramMediaFetchResult Soft(InstagramFetchStatus status, string code, string message) => new()
    {
        Status = status,
        ErrorCode = code,
        Message = message
    };
}

public interface IInstagramGraphClient
{
    bool IsConfigured { get; }

    Task<InstagramMediaFetchResult> FetchOwnMediaAsync(
        int? limit = null,
        bool includeInsights = true,
        CancellationToken cancellationToken = default);
}
