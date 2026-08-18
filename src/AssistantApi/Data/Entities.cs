namespace AssistantApi.Data;

/// <summary>Shared user profile for harness memory (ADR-008).</summary>
public sealed class UserProfileEntity
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Locale { get; set; }
    public string? Timezone { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Per-domain harness episode «задача→результат» (ADR-008).</summary>
public sealed class HarnessEpisodeEntity
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public string? ConversationId { get; set; }
    public string? TraceId { get; set; }
}

/// <summary>Instagram + VK research schedule settings (Phase 4/6). No IG/VK tokens here.</summary>
public sealed class ResearchSettingsEntity
{
    public string UserId { get; set; } = string.Empty;
    public string? InstagramHandle { get; set; }
    /// <summary>JSON allowlist of open VK communities (screen_name / owner_id). Never tokens.</summary>
    public string? VkCommunitiesJson { get; set; }
    public bool Enabled { get; set; }
    public int CadenceDays { get; set; } = 14;
    public string? Timezone { get; set; }
    public string? NotifyChatId { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    /// <summary>Last soft failure (no tokens). Cleared on success.</summary>
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Idempotent successful research run window (userId + periodKey).</summary>
public sealed class ResearchScheduleRunEntity
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    /// <summary>UTC date of due NextRunAt window, e.g. 2026-08-17.</summary>
    public string PeriodKey { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
}

/// <summary>Normalized feed snapshot JSON (ADR-010). Cap last K in store; no raw IG token.</summary>
public sealed class ResearchSnapshotEntity
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

/// <summary>14-day research plan JSON (items: date/caption/hashtags/imagePrompt/…). Not RAG.</summary>
public sealed class ResearchPlanEntity
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly WindowStart { get; set; }
    public DateOnly WindowEnd { get; set; }
    public string PayloadJson { get; set; } = "{}";
}
