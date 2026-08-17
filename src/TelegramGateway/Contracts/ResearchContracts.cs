namespace TelegramGateway.Contracts;

/// <summary>Research settings DTO mirrored from assistant-api — never includes IG token.</summary>
public sealed class ResearchSettingsDto
{
    public string UserId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? InstagramHandle { get; set; }
    public int CadenceDays { get; set; } = 14;
    public string? Timezone { get; set; }
    public string? NotifyChatId { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class ResearchSettingsUpdateRequest
{
    public string UserId { get; set; } = string.Empty;
    public bool? Enabled { get; set; }
    public string? InstagramHandle { get; set; }
    public int? CadenceDays { get; set; }
    public string? Timezone { get; set; }
    public string? NotifyChatId { get; set; }
}

public sealed class ResearchRunRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? NotifyChatId { get; set; }
}

public sealed class ResearchRunResponse
{
    public string Outcome { get; set; } = string.Empty;
    public string? PeriodKey { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public ResearchSettingsDto Settings { get; set; } = new();
    public string? PlanPreview { get; set; }
}

public sealed class ResearchLatestResponse
{
    public ResearchSettingsDto Settings { get; set; } = new();
    public string? SnapshotSummary { get; set; }
    public DateTimeOffset? SnapshotCapturedAt { get; set; }
    public string? PlanPreview { get; set; }
    public DateTimeOffset? PlanCreatedAt { get; set; }
    public int? PlanItemCount { get; set; }
    public DateOnly? PlanWindowStart { get; set; }
    public DateOnly? PlanWindowEnd { get; set; }
}

public sealed class InternalNotifyRequest
{
    public long ChatId { get; set; }
    public string Text { get; set; } = string.Empty;
}
