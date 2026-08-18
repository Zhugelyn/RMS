using AssistantApi.Instagram;
using AssistantApi.Security;
using Microsoft.Extensions.Logging;

namespace AssistantApi.Research;

public interface IResearchApiService
{
    Task<ResearchSettingsDto> GetSettingsAsync(string userId, CancellationToken cancellationToken);
    Task<ResearchSettingsDto> UpsertSettingsAsync(ResearchSettingsUpdateRequest request, CancellationToken cancellationToken);
    Task<ResearchRunResponse> RunNowAsync(ResearchRunRequest request, CancellationToken cancellationToken);
    Task<ResearchLatestResponse> GetLatestAsync(string userId, CancellationToken cancellationToken);
}

public sealed class ResearchApiService : IResearchApiService
{
    private readonly IResearchSettingsStore _settings;
    private readonly IResearchArtifactStore _artifacts;
    private readonly IResearchSchedulerJob _scheduler;
    private readonly IInstagramTokenStore _tokens;
    private readonly ILogger<ResearchApiService> _logger;

    public ResearchApiService(
        IResearchSettingsStore settings,
        IResearchArtifactStore artifacts,
        IResearchSchedulerJob scheduler,
        IInstagramTokenStore tokens,
        ILogger<ResearchApiService> logger)
    {
        _settings = settings;
        _artifacts = artifacts;
        _scheduler = scheduler;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<ResearchSettingsDto> GetSettingsAsync(string userId, CancellationToken cancellationToken)
    {
        var existing = await _settings.GetAsync(userId, cancellationToken);
        return ToDto(existing ?? DefaultSettings(userId));
    }

    public async Task<ResearchSettingsDto> UpsertSettingsAsync(
        ResearchSettingsUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var userId = request.UserId.Trim();
        var existing = await _settings.GetAsync(userId, cancellationToken) ?? DefaultSettings(userId);

        var handle = request.InstagramHandle is null
            ? existing.InstagramHandle
            : NormalizeHandle(request.InstagramHandle);

        var timezone = request.Timezone is null
            ? existing.Timezone
            : NormalizeTimezone(request.Timezone);

        var notifyChatId = request.NotifyChatId is null
            ? existing.NotifyChatId
            : NormalizeNotifyChatId(request.NotifyChatId);

        var enabled = request.Enabled ?? existing.Enabled;
        var cadence = request.CadenceDays is { } days
            ? ResearchCadence.NormalizeDays(days)
            : ResearchCadence.NormalizeDays(existing.CadenceDays);

        var nextRunAt = existing.NextRunAt;
        if (enabled && nextRunAt is null)
        {
            nextRunAt = DateTimeOffset.UtcNow;
        }

        if (!enabled)
        {
            // Keep nextRunAt for resume; do not invent a new window while off.
        }

        var updated = new ResearchSettings
        {
            UserId = userId,
            InstagramHandle = handle,
            Enabled = enabled,
            CadenceDays = cadence,
            Timezone = timezone,
            NotifyChatId = notifyChatId,
            NextRunAt = nextRunAt,
            LastRunAt = existing.LastRunAt,
            LastError = existing.LastError
        };

        await _settings.UpsertAsync(updated, cancellationToken);
        return ToDto(updated);
    }

    public async Task<ResearchRunResponse> RunNowAsync(
        ResearchRunRequest request,
        CancellationToken cancellationToken)
    {
        var userId = request.UserId.Trim();
        var existing = await _settings.GetAsync(userId, cancellationToken) ?? DefaultSettings(userId);

        if (!string.IsNullOrWhiteSpace(request.NotifyChatId))
        {
            existing = new ResearchSettings
            {
                UserId = existing.UserId,
                InstagramHandle = existing.InstagramHandle,
                Enabled = existing.Enabled,
                CadenceDays = existing.CadenceDays,
                Timezone = existing.Timezone,
                NotifyChatId = NormalizeNotifyChatId(request.NotifyChatId),
                NextRunAt = existing.NextRunAt,
                LastRunAt = existing.LastRunAt,
                LastError = existing.LastError
            };
            await _settings.UpsertAsync(existing, cancellationToken);
        }

        if (!_tokens.HasToken)
        {
            var settingsNoToken = ToDto(existing);
            return new ResearchRunResponse
            {
                Outcome = nameof(ResearchScheduleOutcome.NoOp),
                Message = "Instagram token не настроен на сервере (env/secret store). Run пропущен.",
                Settings = settingsNoToken
            };
        }

        var now = DateTimeOffset.UtcNow;
        // Force a due window so ProcessOneAsync captures even if schedule was in the future.
        var forced = new ResearchSettings
        {
            UserId = existing.UserId,
            InstagramHandle = existing.InstagramHandle,
            Enabled = existing.Enabled,
            CadenceDays = ResearchCadence.NormalizeDays(existing.CadenceDays),
            Timezone = existing.Timezone,
            NotifyChatId = existing.NotifyChatId,
            NextRunAt = existing.NextRunAt is { } due && due <= now ? due : now,
            LastRunAt = existing.LastRunAt,
            LastError = existing.LastError
        };

        ResearchScheduleTickResult tick;
        try
        {
            tick = await _scheduler.ProcessOneAsync(forced, now, cancellationToken, force: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research run-now failed userId={UserId}", userId);
            var afterFail = await GetSettingsAsync(userId, cancellationToken);
            return new ResearchRunResponse
            {
                Outcome = nameof(ResearchScheduleOutcome.Failed),
                ErrorCode = "research-run-exception",
                Message = "Research run failed.",
                Settings = afterFail
            };
        }

        var settings = await GetSettingsAsync(userId, cancellationToken);
        var latest = await _artifacts.GetLatestPlanAsync(userId, cancellationToken);
        return new ResearchRunResponse
        {
            Outcome = tick.Outcome.ToString(),
            PeriodKey = tick.PeriodKey,
            ErrorCode = tick.ErrorCode,
            Message = tick.Outcome switch
            {
                ResearchScheduleOutcome.Captured => "Research выполнен.",
                ResearchScheduleOutcome.SkippedAlreadyRan => "Уже был успешный run в этом окне.",
                ResearchScheduleOutcome.Failed => "Research не удался (см. lastError).",
                ResearchScheduleOutcome.NoOp => "Run пропущен (token/disabled).",
                _ => tick.Outcome.ToString()
            },
            Settings = settings,
            PlanPreview = ResearchPlanPreview.Format(latest)
        };
    }

    public async Task<ResearchLatestResponse> GetLatestAsync(string userId, CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(userId, cancellationToken);
        var snapshot = await _artifacts.GetLatestSnapshotAsync(userId, cancellationToken);
        var plan = await _artifacts.GetLatestPlanAsync(userId, cancellationToken);

        return new ResearchLatestResponse
        {
            Settings = settings,
            SnapshotSummary = snapshot?.Summary,
            SnapshotCapturedAt = snapshot?.CapturedAt,
            PlanPreview = ResearchPlanPreview.Format(plan),
            PlanCreatedAt = plan?.CreatedAt,
            PlanItemCount = plan?.Items.Count,
            PlanWindowStart = plan?.WindowStart,
            PlanWindowEnd = plan?.WindowEnd,
            Analytics = BuildAnalytics(snapshot),
            Posts = BuildPosts(snapshot),
            Items = BuildItems(plan)
        };
    }

    private static ResearchAnalyticsDto? BuildAnalytics(ResearchSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        long? impressions = null;
        long? reach = null;
        long? engagement = null;
        long? saved = null;

        foreach (var post in snapshot.Posts)
        {
            if (post.Impressions is { } i)
            {
                impressions = (impressions ?? 0) + i;
            }

            if (post.Reach is { } r)
            {
                reach = (reach ?? 0) + r;
            }

            if (post.Engagement is { } e)
            {
                engagement = (engagement ?? 0) + e;
            }

            if (post.Saved is { } s)
            {
                saved = (saved ?? 0) + s;
            }
        }

        return new ResearchAnalyticsDto
        {
            Impressions = impressions,
            Reach = reach,
            Engagement = engagement,
            Saved = saved,
            PostCount = snapshot.PostCount > 0 ? snapshot.PostCount : snapshot.Posts.Count,
            CapturedAt = snapshot.CapturedAt
        };
    }

    private static IReadOnlyList<ResearchPostMetricDto> BuildPosts(ResearchSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Posts.Count == 0)
        {
            return Array.Empty<ResearchPostMetricDto>();
        }

        return snapshot.Posts
            .Take(ResearchArtifactLimits.MaxPostsPerSnapshot)
            .Select(p => new ResearchPostMetricDto
            {
                MediaId = p.MediaId,
                Caption = Truncate(p.Caption, 160),
                Timestamp = p.Timestamp,
                Impressions = p.Impressions,
                Reach = p.Reach,
                Engagement = p.Engagement,
                Saved = p.Saved
            })
            .ToList();
    }

    private static IReadOnlyList<ResearchPlanItemDto> BuildItems(ResearchPlan? plan)
    {
        if (plan is null || plan.Items.Count == 0)
        {
            return Array.Empty<ResearchPlanItemDto>();
        }

        return plan.Items
            .Take(ResearchArtifactLimits.PlanDays)
            .Select(i => new ResearchPlanItemDto
            {
                Date = i.Date,
                Caption = i.Caption,
                Hashtags = i.Hashtags,
                ImagePrompt = i.ImagePrompt,
                Status = i.Status.ToString().ToLowerInvariant(),
                MediaPath = i.MediaPath,
                ImageUrl = null
            })
            .ToList();
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value
        : value.Length <= max ? value
        : value[..max] + "…";

    private static ResearchSettings DefaultSettings(string userId) => new()
    {
        UserId = userId,
        Enabled = false,
        CadenceDays = ResearchSettingsDefaults.DefaultCadenceDays
    };

    private static ResearchSettingsDto ToDto(ResearchSettings s) => new()
    {
        UserId = s.UserId,
        Enabled = s.Enabled,
        InstagramHandle = s.InstagramHandle,
        CadenceDays = ResearchCadence.NormalizeDays(s.CadenceDays),
        Timezone = s.Timezone,
        NotifyChatId = s.NotifyChatId,
        NextRunAt = s.NextRunAt,
        LastRunAt = s.LastRunAt,
        LastError = s.LastError
    };

    private static string? NormalizeHandle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var h = raw.Trim().TrimStart('@');
        if (h.Length > 64)
        {
            h = h[..64];
        }

        // Reject secret-looking fragments in handle field.
        if (LooksLikeSecretFragment(h))
        {
            throw new ResearchValidationException("Instagram handle looks like a secret; IG token must not be sent here.");
        }

        return h;
    }

    private static string? NormalizeTimezone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var tz = raw.Trim();
        if (tz.Length > 64)
        {
            tz = tz[..64];
        }

        if (LooksLikeSecretFragment(tz))
        {
            throw new ResearchValidationException("Timezone looks like a secret.");
        }

        return tz;
    }

    private static string? NormalizeNotifyChatId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var id = raw.Trim();
        if (id.Length > 32)
        {
            id = id[..32];
        }

        // Telegram chat ids are numeric (optionally negative for groups).
        if (!long.TryParse(id, out _))
        {
            throw new ResearchValidationException("notifyChatId must be a Telegram chat id.");
        }

        return id;
    }

    private static bool LooksLikeSecretFragment(string text) =>
        text.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("service_token", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("VK__SERVICETOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("IGQVJ", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("EAA", StringComparison.Ordinal) ||
        text.Contains("sk-", StringComparison.OrdinalIgnoreCase);
}

public sealed class ResearchValidationException : Exception
{
    public ResearchValidationException(string message) : base(message)
    {
    }
}
