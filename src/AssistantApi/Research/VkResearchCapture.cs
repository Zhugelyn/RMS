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
    public int TargetsAttempted { get; init; }
    public int TargetsOk { get; init; }
}

/// <summary>
/// VK wall → snapshot (source=vk) + 14-day plan + marketing episode.
/// Soft-fail persist; no media download (phase6-vk-media).
/// Settings allowlist targets via <see cref="CaptureAllowlistAsync"/>.
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

    /// <summary>Fetch all allowlisted communities; merge posts (cap ≤50) into one snapshot.</summary>
    Task<VkResearchCaptureResult> CaptureAllowlistAsync(
        string userId,
        IReadOnlyList<VkCommunityTarget> targets,
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

    public Task<VkResearchCaptureResult> CaptureAsync(
        string userId,
        string? screenName = null,
        long? ownerId = null,
        string? conversationId = null,
        string? traceId = null,
        CancellationToken cancellationToken = default)
    {
        if (ownerId is null && string.IsNullOrWhiteSpace(screenName))
        {
            return Task.FromResult(new VkResearchCaptureResult
            {
                FetchStatus = VkFetchStatus.SoftError,
                ErrorCode = "vk-target-missing",
                Message = "VK capture requires screenName or ownerId (allowlist)."
            });
        }

        var targets = VkCommunityAllowlist.Normalize(
        [
            new VkCommunityTarget { ScreenName = screenName, OwnerId = ownerId }
        ]);
        return CaptureAllowlistAsync(userId, targets, conversationId, traceId, cancellationToken);
    }

    public async Task<VkResearchCaptureResult> CaptureAllowlistAsync(
        string userId,
        IReadOnlyList<VkCommunityTarget> targets,
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

        var allowlist = VkCommunityAllowlist.Normalize(targets);
        if (allowlist.Count == 0)
        {
            return new VkResearchCaptureResult
            {
                FetchStatus = VkFetchStatus.SoftError,
                ErrorCode = "vk-allowlist-empty",
                Message = "VK allowlist is empty."
            };
        }

        var mergedPosts = new List<VkWallPost>();
        var skippedDonut = 0;
        var targetsOk = 0;
        var lastStatus = VkFetchStatus.SoftError;
        string? lastCode = null;
        string? lastMessage = null;
        var labels = new List<string>();

        foreach (var target in allowlist)
        {
            VkWallFetchResult fetch;
            try
            {
                fetch = await _vk.GetWallAsync(
                    screenName: target.ScreenName,
                    ownerId: target.OwnerId,
                    count: ResearchArtifactLimits.MaxPostsPerSnapshot,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "VK wall fetch failed userId={UserId} target={Target}",
                    userId,
                    target.Display);
                lastStatus = VkFetchStatus.SoftError;
                lastCode = "vk-fetch-failed";
                lastMessage = "VK wall fetch failed.";
                continue;
            }

            lastStatus = fetch.Status;
            lastCode = fetch.ErrorCode;
            lastMessage = fetch.Message;
            skippedDonut += fetch.SkippedDonutCount;

            if (fetch.Status is VkFetchStatus.Ok or VkFetchStatus.SkippedNoToken
                || fetch.Posts.Count > 0)
            {
                if (fetch.Status == VkFetchStatus.Ok || fetch.Posts.Count > 0)
                {
                    targetsOk++;
                }

                labels.Add(fetch.ScreenName ?? target.Display);
                foreach (var post in fetch.Posts)
                {
                    if (mergedPosts.Count >= ResearchArtifactLimits.MaxPostsPerSnapshot)
                    {
                        break;
                    }

                    mergedPosts.Add(post);
                }
            }

            if (mergedPosts.Count >= ResearchArtifactLimits.MaxPostsPerSnapshot)
            {
                break;
            }
        }

        var aggregateFetch = new VkWallFetchResult
        {
            Status = targetsOk > 0
                ? VkFetchStatus.Ok
                : lastStatus,
            ErrorCode = targetsOk > 0 ? null : lastCode,
            Message = targetsOk > 0
                ? $"VK allowlist capture: {targetsOk}/{allowlist.Count} ok, posts={mergedPosts.Count}"
                : lastMessage ?? "VK allowlist capture produced no posts.",
            OwnerId = allowlist[0].OwnerId,
            ScreenName = labels.Count > 0
                ? string.Join(',', labels.Take(3))
                : allowlist[0].ScreenName,
            Posts = mergedPosts
                .OrderByDescending(p => p.Date ?? DateTimeOffset.MinValue)
                .Take(ResearchArtifactLimits.MaxPostsPerSnapshot)
                .ToList(),
            SkippedDonutCount = skippedDonut
        };

        var snapshot = ResearchSnapshotBuilder.FromVkFetch(userId, aggregateFetch);
        var plan = snapshot.Posts.Count > 0
            ? ResearchPlanBuilder.FromSnapshot(userId, snapshot)
            : ResearchPlanBuilder.EmptyDraft(userId, aggregateFetch.Status.ToString());

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
            var targetLabel = labels.Count > 0
                ? string.Join(',', labels.Take(3))
                : $"{allowlist.Count} targets";
            var task = $"VK research {targetLabel}: {snapshot.PostCount} posts → план {ResearchArtifactLimits.PlanDays}д";
            var result =
                $"source=vk snapshot={(snapshotSaved ? "ok" : "fail")} plan={(planSaved ? "ok" : "fail")} " +
                $"status={aggregateFetch.Status} targets={targetsOk}/{allowlist.Count} " +
                $"donutSkipped={skippedDonut} summary={Trim(snapshot.Summary, 100)}";
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
            FetchStatus = aggregateFetch.Status,
            ErrorCode = aggregateFetch.ErrorCode,
            Message = aggregateFetch.Message,
            SnapshotId = snapshotId,
            PlanId = planId,
            Snapshot = snapshot,
            Plan = plan,
            SkippedDonutCount = skippedDonut,
            TargetsAttempted = allowlist.Count,
            TargetsOk = targetsOk
        };
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";
}
