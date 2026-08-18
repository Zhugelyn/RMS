using System.Text.RegularExpressions;
using AssistantApi.Instagram;

namespace AssistantApi.Research;

/// <summary>
/// Builds a fixed 14-day content plan from the latest snapshot (heuristic, no LLM/RAG).
/// GenerateImage fills mediaPath in phase4-generate-image (soft-fail OK).
/// </summary>
public static class ResearchPlanBuilder
{
    private static readonly string[] DefaultHashtags =
    [
        "красота", "уход", "тренды", "бьюти", "маркетинг"
    ];

    public static ResearchPlan FromSnapshot(
        string userId,
        ResearchSnapshot snapshot,
        DateOnly? windowStart = null,
        DateTimeOffset? createdAt = null)
    {
        var start = windowStart ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var end = start.AddDays(ResearchArtifactLimits.PlanDays - 1);
        var at = createdAt ?? DateTimeOffset.UtcNow;
        var seeds = snapshot.Posts.Count > 0
            ? snapshot.Posts
            : Array.Empty<ResearchSnapshotPost>();

        var items = new List<ResearchPlanItem>(ResearchArtifactLimits.PlanDays);
        for (var i = 0; i < ResearchArtifactLimits.PlanDays; i++)
        {
            var day = start.AddDays(i);
            var seed = seeds.Count == 0 ? null : seeds[i % seeds.Count];
            items.Add(BuildItem(day, i + 1, seed, snapshot.Summary));
        }

        return new ResearchPlan
        {
            UserId = userId,
            CreatedAt = at,
            WindowStart = start,
            WindowEnd = end,
            Items = items
        };
    }

    /// <summary>Empty 14-day draft plan when Graph returned no usable posts.</summary>
    public static ResearchPlan EmptyDraft(string userId, InstagramFetchStatus status, DateOnly? windowStart = null)
    {
        var start = windowStart ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var end = start.AddDays(ResearchArtifactLimits.PlanDays - 1);
        var items = Enumerable.Range(0, ResearchArtifactLimits.PlanDays)
            .Select(i => new ResearchPlanItem
            {
                Date = start.AddDays(i),
                Caption = $"Черновик дня {i + 1}: тема по рынку красоты (источник пуст: {status})",
                Hashtags = DefaultHashtags,
                ImagePrompt = $"Beauty marketing moodboard day {i + 1}, soft natural light, no logos",
                Status = ResearchPlanItemStatus.Draft
            })
            .ToList();

        return new ResearchPlan
        {
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            WindowStart = start,
            WindowEnd = end,
            Items = items
        };
    }

    private static ResearchPlanItem BuildItem(DateOnly day, int dayIndex, ResearchSnapshotPost? seed, string snapshotSummary)
    {
        var hashtags = ExtractHashtags(seed?.Caption);
        if (hashtags.Count == 0)
        {
            hashtags = DefaultHashtags.ToList();
        }

        var theme = !string.IsNullOrWhiteSpace(seed?.Caption)
            ? Trim(seed!.Caption!, 120)
            : Trim(snapshotSummary, 120);
        if (string.IsNullOrWhiteSpace(theme))
        {
            theme = "тренды рынка красоты";
        }

        var caption = $"День {dayIndex}/14: {theme}";
        var imagePrompt =
            $"Instagram still for beauty marketing day {dayIndex}: {Trim(theme, 100)}. " +
            $"Style hint: {seed?.VisualNotes ?? seed?.MediaType ?? "IMAGE"}. No brand logos.";

        return new ResearchPlanItem
        {
            Date = day,
            Caption = Trim(caption, ResearchArtifactLimits.CaptionMaxChars)!,
            Hashtags = hashtags,
            ImagePrompt = Trim(imagePrompt, 400)!,
            MediaPath = null,
            TelegramFileId = null,
            Status = ResearchPlanItemStatus.Draft
        };
    }

    private static List<string> ExtractHashtags(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return new List<string>();
        }

        return Regex.Matches(caption, @"#([\p{L}\p{N}_]+)")
            .Select(m => m.Groups[1].Value)
            .Where(t => t.Length is >= 2 and <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var t = value.Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }
}
