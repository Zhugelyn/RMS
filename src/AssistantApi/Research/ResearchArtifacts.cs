using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssistantApi.Research;

/// <summary>Normalized feed post for research snapshot (ADR-010). No raw IG token, no embeddings.</summary>
public sealed class ResearchSnapshotPost
{
    public string MediaId { get; init; } = string.Empty;
    public string? Caption { get; init; }
    public string? MediaType { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public string? Permalink { get; init; }
    /// <summary>Short visual/content note — not image bytes.</summary>
    public string? VisualNotes { get; init; }
    public long? Impressions { get; init; }
    public long? Reach { get; init; }
    public long? Engagement { get; init; }
    public long? Saved { get; init; }
}

/// <summary>Feed snapshot payload persisted in research_snapshots.PayloadJson.</summary>
public sealed class ResearchSnapshot
{
    public long Id { get; init; }
    public string UserId { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public IReadOnlyList<ResearchSnapshotPost> Posts { get; init; } = Array.Empty<ResearchSnapshotPost>();
    /// <summary>Short human-readable summary for marketing pack inject (not full history).</summary>
    public string Summary { get; init; } = string.Empty;
    public int PostCount { get; init; }
    public string? SourceStatus { get; init; }
}

public enum ResearchPlanItemStatus
{
    Draft,
    Ready,
    Published,
    Skipped
}

/// <summary>One day in the 14-day research content plan.</summary>
public sealed class ResearchPlanItem
{
    public DateOnly Date { get; init; }
    public string Caption { get; init; } = string.Empty;
    public IReadOnlyList<string> Hashtags { get; init; } = Array.Empty<string>();
    public string ImagePrompt { get; init; } = string.Empty;
    public string? MediaPath { get; init; }
    public string? TelegramFileId { get; init; }
    public ResearchPlanItemStatus Status { get; init; } = ResearchPlanItemStatus.Draft;
}

/// <summary>14-day plan payload in research_plans.PayloadJson (ADR-010 ≠ RAG).</summary>
public sealed class ResearchPlan
{
    public long Id { get; init; }
    public string UserId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateOnly WindowStart { get; init; }
    public DateOnly WindowEnd { get; init; }
    public IReadOnlyList<ResearchPlanItem> Items { get; init; } = Array.Empty<ResearchPlanItem>();
}

public static class ResearchArtifactLimits
{
    public const int SnapshotCapPerUser = 5;
    public const int PlanDays = 14;
    public const int PlanCapPerUser = 3;
    public const int InjectMaxChars = 1600;
    public const int SummaryMaxChars = 480;
    public const int CaptionMaxChars = 280;
    public const int VisualNotesMaxChars = 160;
}

public static class ResearchArtifactJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false
    };

    public static string SerializeSnapshot(ResearchSnapshot snapshot) =>
        JsonSerializer.Serialize(new SnapshotPayload
        {
            Posts = snapshot.Posts,
            Summary = snapshot.Summary,
            PostCount = snapshot.PostCount,
            SourceStatus = snapshot.SourceStatus
        }, Options);

    public static string SerializePlan(ResearchPlan plan) =>
        JsonSerializer.Serialize(new PlanPayload { Items = plan.Items }, Options);

    public static SnapshotPayload DeserializeSnapshot(string json) =>
        JsonSerializer.Deserialize<SnapshotPayload>(json, Options) ?? new SnapshotPayload();

    public static PlanPayload DeserializePlan(string json) =>
        JsonSerializer.Deserialize<PlanPayload>(json, Options) ?? new PlanPayload();

    public sealed class SnapshotPayload
    {
        public IReadOnlyList<ResearchSnapshotPost> Posts { get; init; } = Array.Empty<ResearchSnapshotPost>();
        public string Summary { get; init; } = string.Empty;
        public int PostCount { get; init; }
        public string? SourceStatus { get; init; }
    }

    public sealed class PlanPayload
    {
        public IReadOnlyList<ResearchPlanItem> Items { get; init; } = Array.Empty<ResearchPlanItem>();
    }
}
