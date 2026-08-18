using AssistantApi.Memory;
using AssistantApi.Packs;
using AssistantApi.Vk;
using Microsoft.Extensions.Logging;

namespace AssistantApi.Research;

public sealed class VkResearchCaptureResult
{
    public bool SnapshotSaved { get; init; }
    public bool PlanSaved { get; init; }
    public bool EpisodeSaved { get; init; }
    public VkFetchStatus FetchStatus { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public long? SnapshotId { get; init; }
    public long? PlanId { get; init; }
    public ResearchSnapshot? Snapshot { get; init; }
    public ResearchPlan? Plan { get; init; }
    public int SkippedDonutCount { get; init; }
}

/// <summary>
/// VK wall → snapshot (source=vk) + 14-day plan + marketing episode.
/// Soft-fail persist; no media download (phase6-vk-media); no settings UI (phase6-vk-settings).
/// </summary>
public interface IVkResearchCapture
{
    Task<VkResearchCaptureResult> CaptureAsync(
        string userId,
        string? screenName = null,
        long? ownerId = null,
        string? conversationId = null,
        string? traceId = null,
        CancellationToken cancellationToken = default);
}

public sealed class VkResearchCapture : IVkResearchCapture
{
    private readonly IVkWallClient _vk;
    private readonly IResearchArtifactStore _artifacts;
    private readonly IHarnessMemoryStore _memory;
    private readonly ILogger<VkResearchCapture> _logger;

    public VkResearchCapture(
        IVkWallClient vk,
        IResearchArtifactStore artifacts,
        IHarnessMemoryStore memory,
        ILogger<VkResearchCapture> logger)
    {
        _vk = vk;
        _artifacts = artifacts;
        _memory = memory;
        _logger = logger;
    }

    public async Task<VkResearchCaptureResult> CaptureAsync(
        string userId,
        string? screenName = null,
        long? ownerId = null,
        string? conversationId = null,
        string? traceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new VkResearchCaptureResult
            {
                FetchStatus = VkFetchStatus.SoftError,
                ErrorCode = "research-user-missing",
                Message = "userId is required for VK research capture."
            };
        }

        if (ownerId is null && string.IsNullOrWhiteSpace(screenName))
        {
            return new VkResearchCaptureResult
            {
                FetchStatus = VkFetchStatus.SoftError,
                ErrorCode = "vk-target-missing",
                Message = "VK capture requires screenName or ownerId (allowlist comes in settings slice)."
            };
        }

        VkWallFetchResult fetch;
        try
        {
            fetch = await _vk.GetWallAsync(
                screenName: screenName,
                ownerId: ownerId,
                count: ResearchArtifactLimits.MaxPostsPerSnapshot,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "VK wall fetch failed userId={UserId}", userId);
            return new VkResearchCaptureResult
            {
                FetchStatus = VkFetchStatus.SoftError,
                ErrorCode = "vk-fetch-failed",
                Message = "VK wall fetch failed."
            };
        }

        var snapshot = ResearchSnapshotBuilder.FromVkFetch(userId, fetch);
        var plan = snapshot.Posts.Count > 0
            ? ResearchPlanBuilder.FromSnapshot(userId, snapshot)
            : ResearchPlanBuilder.EmptyDraft(userId, fetch.Status.ToString());

        long? snapshotId = null;
        var snapshotSaved = false;
        try
        {
            snapshotId = await _artifacts.SaveSnapshotAsync(snapshot, cancellationToken);
            snapshotSaved = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "VK research snapshot persist failed userId={UserId}", userId);
        }

        long? planId = null;
        var planSaved = false;
        try
        {
            planId = await _artifacts.SavePlanAsync(plan, cancellationToken);
            planSaved = true;
            plan = new ResearchPlan
            {
                Id = planId.Value,
                UserId = plan.UserId,
                CreatedAt = plan.CreatedAt,
                WindowStart = plan.WindowStart,
                WindowEnd = plan.WindowEnd,
                Items = plan.Items
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "VK research plan persist failed userId={UserId}", userId);
        }

        var episodeSaved = false;
        try
        {
            var target = !string.IsNullOrWhiteSpace(fetch.ScreenName)
                ? fetch.ScreenName!
                : ownerId?.ToString() ?? screenName ?? "vk";
            var task = $"VK research {target}: {snapshot.PostCount} posts → план {ResearchArtifactLimits.PlanDays}д";
            var result =
                $"source=vk snapshot={(snapshotSaved ? "ok" : "fail")} plan={(planSaved ? "ok" : "fail")} " +
                $"status={fetch.Status} donutSkipped={fetch.SkippedDonutCount} summary={Trim(snapshot.Summary, 100)}";
            await _memory.AddEpisodeAsync(new HarnessEpisode
            {
                UserId = userId,
                Domain = PackIds.Marketing,
                Task = Trim(task, 180),
                Result = Trim(result, 220),
                At = DateTimeOffset.UtcNow,
                ConversationId = conversationId,
                TraceId = traceId
            }, cancellationToken);
            episodeSaved = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "VK research episode persist failed userId={UserId}", userId);
        }

        return new VkResearchCaptureResult
        {
            SnapshotSaved = snapshotSaved,
            PlanSaved = planSaved,
            EpisodeSaved = episodeSaved,
            FetchStatus = fetch.Status,
            ErrorCode = fetch.ErrorCode,
            Message = fetch.Message,
            SnapshotId = snapshotId,
            PlanId = planId,
            Snapshot = snapshot,
            Plan = plan,
            SkippedDonutCount = fetch.SkippedDonutCount
        };
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";
}
