using System.Text;
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

/// <summary>Known research feed sources (additive; omit/null = legacy Instagram).</summary>
public static class ResearchSources
{
    public const string Instagram = "instagram";
    public const string Vk = "vk";
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
    /// <summary>Additive feed source: <c>vk</c> | <c>instagram</c> | null (legacy IG). Not RAG.</summary>
    public string? Source { get; init; }
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
    /// <summary>Retention: keep last K snapshots per user (TTL-style cap).</summary>
    public const int SnapshotCapPerUser = 5;

    public const int PlanDays = 14;

    /// <summary>Retention: keep last K plans per user.</summary>
    public const int PlanCapPerUser = 3;

    public const int InjectMaxChars = 1600;
    public const int SummaryMaxChars = 480;
    public const int CaptionMaxChars = 280;
    public const int VisualNotesMaxChars = 160;

    /// <summary>Hard cap posts stored in one snapshot payload.</summary>
    public const int MaxPostsPerSnapshot = 50;

    /// <summary>Max UTF-8 bytes for snapshot/plan PayloadJson before reject.</summary>
    public const int MaxPayloadBytes = 256 * 1024;
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

    public static string SerializeSnapshot(ResearchSnapshot snapshot)
    {
        var posts = snapshot.Posts.Take(ResearchArtifactLimits.MaxPostsPerSnapshot).ToList();
        var json = JsonSerializer.Serialize(new SnapshotPayload
        {
            Posts = posts,
            Summary = Truncate(snapshot.Summary, ResearchArtifactLimits.SummaryMaxChars),
            PostCount = snapshot.PostCount > 0 ? Math.Min(snapshot.PostCount, posts.Count) : posts.Count,
            SourceStatus = snapshot.SourceStatus,
            Source = snapshot.Source
        }, Options);
        EnsurePayloadSize(json, "snapshot");
        return json;
    }

    public static string SerializePlan(ResearchPlan plan)
    {
        var items = plan.Items.Take(ResearchArtifactLimits.PlanDays).ToList();
        var json = JsonSerializer.Serialize(new PlanPayload { Items = items }, Options);
        EnsurePayloadSize(json, "plan");
        return json;
    }

    public static SnapshotPayload DeserializeSnapshot(string json)
    {
        EnsurePayloadSize(json, "snapshot");
        return JsonSerializer.Deserialize<SnapshotPayload>(json, Options) ?? new SnapshotPayload();
    }

    public static PlanPayload DeserializePlan(string json)
    {
        EnsurePayloadSize(json, "plan");
        return JsonSerializer.Deserialize<PlanPayload>(json, Options) ?? new PlanPayload();
    }

    private static void EnsurePayloadSize(string json, string kind)
    {
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > ResearchArtifactLimits.MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"research {kind} payload exceeds {ResearchArtifactLimits.MaxPayloadBytes} bytes");
        }
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= max ? value
        : value[..max];

    public sealed class SnapshotPayload
    {
        public IReadOnlyList<ResearchSnapshotPost> Posts { get; init; } = Array.Empty<ResearchSnapshotPost>();
        public string Summary { get; init; } = string.Empty;
        public int PostCount { get; init; }
        public string? SourceStatus { get; init; }
        /// <summary>Additive: <c>vk</c> | <c>instagram</c>. Null = legacy Instagram payload.</summary>
        public string? Source { get; init; }
    }

    public sealed class PlanPayload
    {
        public IReadOnlyList<ResearchPlanItem> Items { get; init; } = Array.Empty<ResearchPlanItem>();
    }
}
