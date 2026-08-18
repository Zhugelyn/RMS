namespace TelegramGateway.Contracts;

/// <summary>Research settings DTO mirrored from assistant-api — never includes IG/VK tokens.</summary>
public sealed class ResearchSettingsDto
{
    public string UserId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? InstagramHandle { get; set; }
    public List<VkCommunityTargetDto> VkCommunities { get; set; } = [];
    public int CadenceDays { get; set; } = 14;
    public string? Timezone { get; set; }
    public string? NotifyChatId { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class VkCommunityTargetDto
{
    public string? ScreenName { get; set; }
    public long? OwnerId { get; set; }
}

public sealed class ResearchSettingsUpdateRequest
{
    public string UserId { get; set; } = string.Empty;
    public bool? Enabled { get; set; }
    public string? InstagramHandle { get; set; }
    public List<VkCommunityTargetDto>? VkCommunities { get; set; }
    public int? CadenceDays { get; set; }
    public string? Timezone { get; set; }
    public string? NotifyChatId { get; set; }
}

public sealed class ResearchRunRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? NotifyChatId { get; set; }
    /// <summary>Additive: null/instagram → IG; <c>vk</c> → VK allowlist capture.</summary>
    public string? Source { get; set; }
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
    public ResearchAnalyticsDto? Analytics { get; set; }
    public List<ResearchPostMetricDto> Posts { get; set; } = [];
    public List<ResearchPlanItemDto> Items { get; set; } = [];
}

public sealed class ResearchAnalyticsDto
{
    public long? Impressions { get; set; }
    public long? Reach { get; set; }
    public long? Engagement { get; set; }
    public long? Saved { get; set; }
    public int PostCount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}

public sealed class ResearchPostMetricDto
{
    public string? MediaId { get; set; }
    public string? Caption { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public long? Impressions { get; set; }
    public long? Reach { get; set; }
    public long? Engagement { get; set; }
    public long? Saved { get; set; }
}

public sealed class ResearchPlanItemDto
{
    public DateOnly Date { get; set; }
    public string Caption { get; set; } = string.Empty;
    public List<string> Hashtags { get; set; } = [];
    public string ImagePrompt { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public string? MediaPath { get; set; }
    /// <summary>Gateway media proxy path — never a raw volume file URL.</summary>
    public string? ImageUrl { get; set; }
}

public sealed class InternalNotifyRequest
{
    public long ChatId { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>Relative paths under image volume (research-media/...).</summary>
    public List<string>? PhotoPaths { get; set; }
    /// <summary>Optional override; defaults to Research:ImageVolumePath on gateway.</summary>
    public string? ImageVolumePath { get; set; }
}
