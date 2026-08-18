namespace AssistantApi.Vk;

public enum VkFetchStatus
{
    Ok,
    /// <summary>No service token configured — honest skip/stub path.</summary>
    SkippedNoToken,
    /// <summary>Screen name could not be resolved to a community/user.</summary>
    SkippedUnresolved,
    /// <summary>Closed wall / private community / Donut-only wall.</summary>
    SoftSkippedClosed,
    RateLimited,
    TokenInvalid,
    SoftError
}

public sealed class VkPhotoAttachment
{
    public long PhotoId { get; init; }
    public long OwnerId { get; init; }
    /// <summary>Largest allowlisted CDN URL (*.userapi.com). May be null if no size passed SSRF guard.</summary>
    public string? Url { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}

public sealed class VkWallPost
{
    public long Id { get; init; }
    public long OwnerId { get; init; }
    public string? Text { get; init; }
    public DateTimeOffset? Date { get; init; }
    public bool IsDonut { get; init; }
    public IReadOnlyList<VkPhotoAttachment> Photos { get; init; } = Array.Empty<VkPhotoAttachment>();
}

public sealed class VkResolveResult
{
    public VkFetchStatus Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    /// <summary>Resolved object type: group | page | user | application | …</summary>
    public string? ObjectType { get; init; }
    public long? ObjectId { get; init; }
    /// <summary>owner_id for wall.get (negative for groups/pages).</summary>
    public long? OwnerId { get; init; }

    public static VkResolveResult SkipNoToken() => new()
    {
        Status = VkFetchStatus.SkippedNoToken,
        ErrorCode = "vk-token-missing",
        Message = "VK service token is not configured; resolve skipped."
    };

    public static VkResolveResult Soft(VkFetchStatus status, string code, string message) => new()
    {
        Status = status,
        ErrorCode = code,
        Message = message
    };
}

public sealed class VkWallFetchResult
{
    public VkFetchStatus Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public long? OwnerId { get; init; }
    public string? ScreenName { get; init; }
    public IReadOnlyList<VkWallPost> Posts { get; init; } = Array.Empty<VkWallPost>();
    public int SkippedDonutCount { get; init; }

    public static VkWallFetchResult SkipNoToken() => new()
    {
        Status = VkFetchStatus.SkippedNoToken,
        ErrorCode = "vk-token-missing",
        Message = "VK service token is not configured; wall fetch skipped."
    };

    public static VkWallFetchResult Soft(VkFetchStatus status, string code, string message) => new()
    {
        Status = status,
        ErrorCode = code,
        Message = message
    };
}

public interface IVkWallClient
{
    bool IsConfigured { get; }

    Task<VkResolveResult> ResolveScreenNameAsync(
        string screenName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch open community wall. Prefer <paramref name="ownerId"/> when known;
    /// otherwise resolve <paramref name="screenName"/> first.
    /// </summary>
    Task<VkWallFetchResult> GetWallAsync(
        string? screenName = null,
        long? ownerId = null,
        int? count = null,
        CancellationToken cancellationToken = default);
}
