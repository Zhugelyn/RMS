using AssistantApi.Instagram;
using AssistantApi.Security;
using Microsoft.Extensions.Logging;

namespace AssistantApi.Research;

public enum ResearchScheduleOutcome
{
    NoOp,
    SkippedNotDue,
    SkippedAlreadyRan,
    Captured,
    Failed
}

public sealed class ResearchScheduleTickResult
{
    public ResearchScheduleOutcome Outcome { get; init; }
    public string? PeriodKey { get; init; }
    public string? ErrorCode { get; init; }
    public ResearchCaptureResult? Capture { get; init; }
}

/// <summary>
/// Due research_settings → Graph capture → snapshot/plan/episode; idempotent per userId+period.
/// Failures write lastError and never throw to the host loop.
/// </summary>
public interface IResearchSchedulerJob
{
    Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<ResearchScheduleTickResult> ProcessOneAsync(
        ResearchSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool force = false);
}

public sealed class ResearchSchedulerJob : IResearchSchedulerJob
{
    private readonly IResearchSettingsStore _settings;
    private readonly IResearchScheduleRunStore _runs;
    private readonly IInstagramResearchCapture _capture;
    private readonly IInstagramTokenStore _tokens;
    private readonly IResearchNotifyHook _notify;
    private readonly ILogger<ResearchSchedulerJob> _logger;

    public ResearchSchedulerJob(
        IResearchSettingsStore settings,
        IResearchScheduleRunStore runs,
        IInstagramResearchCapture capture,
        IInstagramTokenStore tokens,
        IResearchNotifyHook notify,
        ILogger<ResearchSchedulerJob> logger)
    {
        _settings = settings;
        _runs = runs;
        _capture = capture;
        _tokens = tokens;
        _notify = notify;
        _logger = logger;
    }

    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Without process-level IG token — honest no-op (do not scan due rows / crash).
        if (!_tokens.HasToken)
        {
            return;
        }

        IReadOnlyList<ResearchSettings> due;
        try
        {
            due = await _settings.ListDueAsync(now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research scheduler ListDue failed");
            return;
        }

        foreach (var settings in due)
        {
            try
            {
                await ProcessOneAsync(settings, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Research scheduler process failed userId={UserId}", settings.UserId);
                try
                {
                    await WriteFailureAsync(
                        settings,
                        now,
                        "research-scheduler-exception",
                        "Research scheduler tick failed.",
                        cancellationToken);
                }
                catch (Exception writeEx) when (writeEx is not OperationCanceledException)
                {
                    _logger.LogWarning(writeEx, "Research lastError write failed userId={UserId}", settings.UserId);
                }
            }
        }
    }

    public async Task<ResearchScheduleTickResult> ProcessOneAsync(
        ResearchSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool force = false)
    {
        if (!settings.Enabled && !force)
        {
            return new ResearchScheduleTickResult { Outcome = ResearchScheduleOutcome.NoOp };
        }

        if (!_tokens.HasToken)
        {
            return new ResearchScheduleTickResult { Outcome = ResearchScheduleOutcome.NoOp };
        }

        if (!force && !ResearchCadence.IsDue(settings, now))
        {
            return new ResearchScheduleTickResult { Outcome = ResearchScheduleOutcome.SkippedNotDue };
        }

        var dueAt = force
            ? now
            : settings.NextRunAt!.Value;
        var periodKey = force
            ? $"manual-{now.UtcDateTime:yyyyMMddHHmmss}"
            : ResearchCadence.PeriodKey(dueAt);

        if (!force && await _runs.HasSuccessfulRunAsync(settings.UserId, periodKey, cancellationToken))
        {
            // Second tick same window: do not re-capture; ensure nextRunAt moved.
            if (settings.NextRunAt <= now)
            {
                await UpsertScheduleAsync(
                    settings,
                    nextRunAt: ResearchCadence.AdvanceNextRun(dueAt, settings.CadenceDays),
                    lastRunAt: settings.LastRunAt ?? now,
                    lastError: null,
                    cancellationToken);
            }

            return new ResearchScheduleTickResult
            {
                Outcome = ResearchScheduleOutcome.SkippedAlreadyRan,
                PeriodKey = periodKey
            };
        }

        ResearchCaptureResult capture;
        try
        {
            capture = await _capture.CaptureAsync(
                settings.UserId,
                conversationId: null,
                traceId: $"research-sched-{periodKey}",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research capture threw userId={UserId}", settings.UserId);
            await WriteFailureAsync(
                settings,
                now,
                "research-capture-failed",
                "Instagram research capture failed.",
                cancellationToken);
            await SafeNotifyAsync(settings, periodKey, success: false, "research-capture-failed", cancellationToken);
            return new ResearchScheduleTickResult
            {
                Outcome = ResearchScheduleOutcome.Failed,
                PeriodKey = periodKey,
                ErrorCode = "research-capture-failed"
            };
        }

        if (IsCaptureSuccess(capture))
        {
            // Soft-fail GenerateImage: plan without pictures, job still Captured (ADR-011).
            var softImageError = capture.ImageErrorCode;
            await _runs.MarkSuccessfulAsync(settings.UserId, periodKey, now, cancellationToken);
            await UpsertScheduleAsync(
                settings,
                nextRunAt: ResearchCadence.AdvanceNextRun(dueAt, settings.CadenceDays),
                lastRunAt: now,
                lastError: softImageError,
                cancellationToken);
            await SafeNotifyAsync(
                settings,
                periodKey,
                success: true,
                softImageError,
                cancellationToken,
                photoPaths: capture.ImagePaths);
            return new ResearchScheduleTickResult
            {
                Outcome = ResearchScheduleOutcome.Captured,
                PeriodKey = periodKey,
                Capture = capture,
                ErrorCode = softImageError
            };
        }

        // Graph/token soft-fail: lastError, keep previous snapshot, do not advance / mark period.
        var code = capture.ErrorCode ?? "research-fetch-failed";
        var message = SanitizeError(capture.Message) ?? "Instagram research fetch failed.";
        await WriteFailureAsync(settings, now, code, message, cancellationToken);
        await SafeNotifyAsync(settings, periodKey, success: false, code, cancellationToken);
        return new ResearchScheduleTickResult
        {
            Outcome = ResearchScheduleOutcome.Failed,
            PeriodKey = periodKey,
            ErrorCode = code,
            Capture = capture
        };
    }

    private static bool IsCaptureSuccess(ResearchCaptureResult capture) =>
        capture.FetchStatus == InstagramFetchStatus.Ok
        && capture.SnapshotSaved
        && capture.PlanSaved;

    private async Task WriteFailureAsync(
        ResearchSettings settings,
        DateTimeOffset now,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        // Keep nextRunAt so the job can retry; do not wipe LastRunAt of a prior success.
        await UpsertScheduleAsync(
            settings,
            nextRunAt: settings.NextRunAt,
            lastRunAt: settings.LastRunAt,
            lastError: TrimError($"{code}: {message}"),
            cancellationToken);
        _ = now;
    }

    private async Task UpsertScheduleAsync(
        ResearchSettings settings,
        DateTimeOffset? nextRunAt,
        DateTimeOffset? lastRunAt,
        string? lastError,
        CancellationToken cancellationToken)
    {
        await _settings.UpsertAsync(new ResearchSettings
        {
            UserId = settings.UserId,
            InstagramHandle = settings.InstagramHandle,
            Enabled = settings.Enabled,
            CadenceDays = settings.CadenceDays,
            Timezone = settings.Timezone,
            NotifyChatId = settings.NotifyChatId,
            NextRunAt = nextRunAt,
            LastRunAt = lastRunAt,
            LastError = lastError
        }, cancellationToken);
    }

    private async Task SafeNotifyAsync(
        ResearchSettings settings,
        string periodKey,
        bool success,
        string? errorCode,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? photoPaths = null)
    {
        try
        {
            await _notify.OnResearchRunAsync(new ResearchNotifyEvent
            {
                UserId = settings.UserId,
                NotifyChatId = settings.NotifyChatId,
                PeriodKey = periodKey,
                Success = success,
                ErrorCode = errorCode,
                Message = success ? "research-ok" : "research-failed",
                PhotoPaths = photoPaths ?? Array.Empty<string>()
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research notify hook failed userId={UserId}", settings.UserId);
        }
    }

    private static string? SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        // Never persist token-looking fragments into lastError.
        if (message.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("IGQVJ", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("EAA", StringComparison.Ordinal))
        {
            return "Instagram research fetch failed.";
        }

        return message;
    }

    private static string TrimError(string value) =>
        value.Length <= 512 ? value : value[..512];
}
