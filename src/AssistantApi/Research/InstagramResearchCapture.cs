using AssistantApi.Instagram;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Packs;
using Microsoft.Extensions.Logging;

namespace AssistantApi.Research;

public sealed class ResearchCaptureResult
{
    public bool SnapshotSaved { get; init; }
    public bool PlanSaved { get; init; }
    public bool EpisodeSaved { get; init; }
    public InstagramFetchStatus FetchStatus { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public long? SnapshotId { get; init; }
    public long? PlanId { get; init; }
    public ResearchSnapshot? Snapshot { get; init; }
    public ResearchPlan? Plan { get; init; }
    /// <summary>Soft image failure code (image-tool-missing) — does not fail capture.</summary>
    public string? ImageErrorCode { get; init; }
    public int ImageCount { get; init; }
    public IReadOnlyList<string> ImagePaths { get; init; } = Array.Empty<string>();
}

/// <summary>
/// After Graph fetch: persist snapshot + 14-day plan + optional GenerateImage + marketing episode.
/// Persist / image failures are soft (logged) — never throw to HTTP chat callers.
/// </summary>
public interface IInstagramResearchCapture
{
    Task<ResearchCaptureResult> CaptureAsync(
        string userId,
        string? conversationId = null,
        string? traceId = null,
        CancellationToken cancellationToken = default);
}

public sealed class InstagramResearchCapture : IInstagramResearchCapture
{
    private readonly IInstagramGraphClient _graph;
    private readonly IResearchArtifactStore _artifacts;
    private readonly IHarnessMemoryStore _memory;
    private readonly IResearchImageGenerator _images;
    private readonly ILogger<InstagramResearchCapture> _logger;

    public InstagramResearchCapture(
        IInstagramGraphClient graph,
        IResearchArtifactStore artifacts,
        IHarnessMemoryStore memory,
        IResearchImageGenerator images,
        ILogger<InstagramResearchCapture> logger)
    {
        _graph = graph;
        _artifacts = artifacts;
        _memory = memory;
        _images = images;
        _logger = logger;
    }

    public async Task<ResearchCaptureResult> CaptureAsync(
        string userId,
        string? conversationId = null,
        string? traceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ResearchCaptureResult
            {
                FetchStatus = InstagramFetchStatus.SoftError,
                ErrorCode = "research-user-missing",
                Message = "userId is required for research capture."
            };
        }

        InstagramMediaFetchResult fetch;
        try
        {
            fetch = await _graph.FetchOwnMediaAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research Graph fetch failed userId={UserId}", userId);
            return new ResearchCaptureResult
            {
                FetchStatus = InstagramFetchStatus.SoftError,
                ErrorCode = "research-fetch-failed",
                Message = "Instagram Graph fetch failed."
            };
        }

        var snapshot = ResearchSnapshotBuilder.FromFetch(userId, fetch);
        var plan = snapshot.Posts.Count > 0
            ? ResearchPlanBuilder.FromSnapshot(userId, snapshot)
            : ResearchPlanBuilder.EmptyDraft(userId, fetch.Status);

        long? snapshotId = null;
        var snapshotSaved = false;
        try
        {
            snapshotId = await _artifacts.SaveSnapshotAsync(snapshot, cancellationToken);
            snapshotSaved = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research snapshot persist failed userId={UserId}", userId);
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
            _logger.LogWarning(ex, "Research plan persist failed userId={UserId}", userId);
        }

        string? imageError = null;
        var imageCount = 0;
        IReadOnlyList<string> imagePaths = Array.Empty<string>();
        if (planSaved)
        {
            try
            {
                var runId = string.IsNullOrWhiteSpace(traceId)
                    ? $"cap-{DateTime.UtcNow:yyyyMMddHHmmss}"
                    : traceId!;
                var img = await _images.GenerateAsync(userId, runId, plan, fetch.Items, cancellationToken);
                if (img.SoftFailed)
                {
                    imageError = img.ErrorCode ?? ResearchImageLimits.SoftFailErrorCode;
                }
                else if (!img.Skipped && img.Plan is not null)
                {
                    plan = img.Plan;
                    planId = plan.Id != 0 ? plan.Id : planId;
                    imageCount = img.ImageCount;
                    imagePaths = img.ImagePaths;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Research GenerateImage soft-fail userId={UserId}", userId);
                imageError = ResearchImageLimits.SoftFailErrorCode;
            }
        }

        var episodeSaved = false;
        try
        {
            var task = $"Research {snapshot.PostCount} posts → план {ResearchArtifactLimits.PlanDays}д";
            var result =
                $"snapshot={(snapshotSaved ? "ok" : "fail")} plan={(planSaved ? "ok" : "fail")} " +
                $"images={imageCount} status={fetch.Status} summary={Trim(snapshot.Summary, 120)}";
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
            _logger.LogWarning(ex, "Research episode persist failed userId={UserId}", userId);
        }

        return new ResearchCaptureResult
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
            ImageErrorCode = imageError,
            ImageCount = imageCount,
            ImagePaths = imagePaths
        };
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";
}
