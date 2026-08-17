namespace AssistantApi.Research;

/// <summary>Public research settings DTO — never includes IG token.</summary>
public sealed class ResearchSettingsDto
{
    public string UserId { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string? InstagramHandle { get; init; }
    public int CadenceDays { get; init; } = ResearchSettingsDefaults.DefaultCadenceDays;
    public string? Timezone { get; init; }
    public string? NotifyChatId { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public string? LastError { get; init; }
}

public sealed class ResearchSettingsUpdateRequest
{
    public string UserId { get; init; } = string.Empty;
    public bool? Enabled { get; init; }
    public string? InstagramHandle { get; init; }
    public int? CadenceDays { get; init; }
    public string? Timezone { get; init; }
    public string? NotifyChatId { get; init; }
}

public sealed class ResearchRunRequest
{
    public string UserId { get; init; } = string.Empty;
    /// <summary>Optional override; bot sets from Telegram chat id.</summary>
    public string? NotifyChatId { get; init; }
}

public sealed class ResearchRunResponse
{
    public string Outcome { get; init; } = string.Empty;
    public string? PeriodKey { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public ResearchSettingsDto Settings { get; init; } = new();
    public string? PlanPreview { get; init; }
}

public sealed class ResearchLatestResponse
{
    public ResearchSettingsDto Settings { get; init; } = new();
    public string? SnapshotSummary { get; init; }
    public DateTimeOffset? SnapshotCapturedAt { get; init; }
    public string? PlanPreview { get; init; }
    public DateTimeOffset? PlanCreatedAt { get; init; }
    public int? PlanItemCount { get; init; }
    public DateOnly? PlanWindowStart { get; init; }
    public DateOnly? PlanWindowEnd { get; init; }
}

public static class ResearchPlanPreview
{
    public const int MaxChars = 1200;

    public static string? Format(ResearchPlan? plan, int maxItems = 5)
    {
        if (plan is null || plan.Items.Count == 0)
        {
            return null;
        }

        var lines = new List<string>
        {
            $"План {plan.WindowStart:yyyy-MM-dd}…{plan.WindowEnd:yyyy-MM-dd} ({plan.Items.Count} дн.)"
        };

        foreach (var item in plan.Items.Take(maxItems))
        {
            var tags = item.Hashtags.Count == 0
                ? ""
                : " " + string.Join(' ', item.Hashtags.Take(3).Select(h => h.StartsWith('#') ? h : "#" + h));
            var caption = item.Caption.Length <= 80 ? item.Caption : item.Caption[..80] + "…";
            lines.Add($"• {item.Date:MM-dd}: {caption}{tags}");
        }

        if (plan.Items.Count > maxItems)
        {
            lines.Add($"…ещё {plan.Items.Count - maxItems}");
        }

        var text = string.Join('\n', lines);
        return text.Length <= MaxChars ? text : text[..MaxChars] + "…";
    }
}
