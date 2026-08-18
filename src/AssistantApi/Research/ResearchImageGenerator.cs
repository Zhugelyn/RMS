using AssistantApi.Instagram;
using AssistantApi.Options;
using AssistantApi.Providers;
using AssistantApi.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantApi.Research;

public sealed class ResearchImageGenerationResult
{
    public bool Attempted { get; init; }
    public bool Skipped { get; init; }
    public string? SkipReason { get; init; }
    public bool SoftFailed { get; init; }
    public string? ErrorCode { get; init; }
    public int ImageCount { get; init; }
    public ResearchPlan? Plan { get; init; }
    public IReadOnlyList<string> ImagePaths { get; init; } = Array.Empty<string>();
}

/// <summary>
/// ADR-011: Cursor GenerateImage via local marketing pack + volume. Not OpenAI Images.
/// Soft-fail (tool missing / 429) → ErrorCode=image-tool-missing; plan without images; does not fail research job.
/// </summary>
public interface IResearchImageGenerator
{
    Task<ResearchImageGenerationResult> GenerateAsync(
        string userId,
        string runId,
        ResearchPlan plan,
        IReadOnlyList<InstagramMediaItem> sourceMedia,
        CancellationToken cancellationToken);
}

public sealed class ResearchImageGenerator : IResearchImageGenerator
{
    private readonly ICursorApiKeyStore _keys;
    private readonly ICursorSdkClient _bridge;
    private readonly IResearchImageWorkspace _workspace;
    private readonly IResearchArtifactStore _artifacts;
    private readonly IOptions<ResearchOptions> _options;
    private readonly IOptions<CursorOptions> _cursor;
    private readonly ILogger<ResearchImageGenerator> _logger;

    public ResearchImageGenerator(
        ICursorApiKeyStore keys,
        ICursorSdkClient bridge,
        IResearchImageWorkspace workspace,
        IResearchArtifactStore artifacts,
        IOptions<ResearchOptions> options,
        IOptions<CursorOptions> cursor,
        ILogger<ResearchImageGenerator> logger)
    {
        _keys = keys;
        _bridge = bridge;
        _workspace = workspace;
        _artifacts = artifacts;
        _options = options;
        _cursor = cursor;
        _logger = logger;
    }

    public async Task<ResearchImageGenerationResult> GenerateAsync(
        string userId,
        string runId,
        ResearchPlan plan,
        IReadOnlyList<InstagramMediaItem> sourceMedia,
        CancellationToken cancellationToken)
    {
        if (!_keys.HasKey || !_keys.TryGetApiKey(out var apiKey))
        {
            return new ResearchImageGenerationResult
            {
                Skipped = true,
                SkipReason = "cursor-key-missing",
                Plan = plan
            };
        }

        if (!_workspace.IsConfigured)
        {
            return new ResearchImageGenerationResult
            {
                Skipped = true,
                SkipReason = "image-volume-missing",
                Plan = plan
            };
        }

        ResearchImageWorkspaceInfo? ws;
        try
        {
            ws = await _workspace.PrepareAsync(runId, plan, sourceMedia, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research image workspace prepare failed userId={UserId}", userId);
            return SoftFail(plan);
        }

        if (ws is null)
        {
            return new ResearchImageGenerationResult
            {
                Skipped = true,
                SkipReason = "image-volume-missing",
                Plan = plan
            };
        }

        var prompt =
            "Follow the generate-research-images skill and plan-prompts.md. " +
            "Use Cursor GenerateImage only (never OpenAI Images/DALL·E). " +
            $"Write at most {ResearchImageLimits.MaxImages} images into out/ as png or jpg or webp. " +
            "Reference photos are under refs/. Reply briefly with how many images you wrote.";

        CursorSdkRunResult bridgeResult;
        try
        {
            bridgeResult = await _bridge.RunAsync(
                new CursorSdkRunRequest(
                    ApiKey: apiKey,
                    Prompt: prompt,
                    AgentId: null,
                    Model: string.IsNullOrWhiteSpace(_cursor.Value.Model) ? "composer-2.5" : _cursor.Value.Model,
                    PackId: _options.Value.PackId,
                    LocalCwd: ws.AbsoluteCwd,
                    CollectImages: true,
                    ImageCap: Math.Clamp(_options.Value.ImageCap, 0, ResearchImageLimits.MaxImages)),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research GenerateImage bridge failed userId={UserId}", userId);
            return SoftFail(plan);
        }
        finally
        {
            // Best-effort: do not keep plaintext key longer than needed (string is immutable — just drop ref).
            apiKey = string.Empty;
        }

        if (string.Equals(bridgeResult.Error, ResearchImageLimits.SoftFailErrorCode, StringComparison.Ordinal) ||
            (bridgeResult.Images.Count == 0 && LooksLikeToolSoftFail(bridgeResult.Text)))
        {
            return SoftFail(plan);
        }

        var images = bridgeResult.Images
            .Take(ResearchImageLimits.MaxImages)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/'))
            .ToList();

        if (images.Count == 0)
        {
            // Agent ran but produced no files — treat as soft image failure, plan stays text-only.
            return SoftFail(plan);
        }

        var updated = ResearchGeneratedImageCollector.ApplyMediaPaths(plan, images, ws.RelativeRoot);
        try
        {
            var id = await _artifacts.SavePlanAsync(updated, cancellationToken);
            updated = new ResearchPlan
            {
                Id = id,
                UserId = updated.UserId,
                CreatedAt = updated.CreatedAt,
                WindowStart = updated.WindowStart,
                WindowEnd = updated.WindowEnd,
                Items = updated.Items
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research plan mediaPath persist soft-fail userId={UserId}", userId);
        }

        return new ResearchImageGenerationResult
        {
            Attempted = true,
            ImageCount = images.Count,
            Plan = updated,
            ImagePaths = updated.Items
                .Select(i => i.MediaPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>()
                .Take(ResearchImageLimits.MaxImages)
                .ToList()
        };
    }

    private static ResearchImageGenerationResult SoftFail(ResearchPlan plan) => new()
    {
        Attempted = true,
        SoftFailed = true,
        ErrorCode = ResearchImageLimits.SoftFailErrorCode,
        Plan = plan,
        ImageCount = 0
    };

    private static bool LooksLikeToolSoftFail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("429", StringComparison.Ordinal)
               || text.Contains("GenerateImage", StringComparison.OrdinalIgnoreCase)
               || text.Contains("image-tool-missing", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>No-op when image generation is not registered (tests / explicit disable).</summary>
public sealed class NoOpResearchImageGenerator : IResearchImageGenerator
{
    public Task<ResearchImageGenerationResult> GenerateAsync(
        string userId,
        string runId,
        ResearchPlan plan,
        IReadOnlyList<InstagramMediaItem> sourceMedia,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ResearchImageGenerationResult
        {
            Skipped = true,
            SkipReason = "noop",
            Plan = plan
        });
}
