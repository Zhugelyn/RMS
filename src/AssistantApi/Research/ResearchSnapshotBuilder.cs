using System.Text;
using AssistantApi.Instagram;

namespace AssistantApi.Research;

/// <summary>Maps Graph media → normalized snapshot (no token, no raw media bytes).</summary>
public static class ResearchSnapshotBuilder
{
    public static ResearchSnapshot FromFetch(
        string userId,
        InstagramMediaFetchResult fetch,
        DateTimeOffset? capturedAt = null)
    {
        var at = capturedAt ?? DateTimeOffset.UtcNow;
        var posts = fetch.Items
            .Take(ResearchArtifactLimits.MaxPostsPerSnapshot)
            .Select(ToPost)
            .ToList();

        return new ResearchSnapshot
        {
            UserId = userId,
            CapturedAt = at,
            Posts = posts,
            PostCount = posts.Count,
            Summary = BuildSummary(posts, fetch.Status),
            SourceStatus = fetch.Status.ToString()
        };
    }

    public static ResearchSnapshotPost ToPost(InstagramMediaItem item)
    {
        var caption = Trim(item.Caption, ResearchArtifactLimits.CaptionMaxChars);
        return new ResearchSnapshotPost
        {
            MediaId = item.Id,
            Caption = caption,
            MediaType = item.MediaType,
            Timestamp = item.Timestamp,
            Permalink = item.Permalink,
            VisualNotes = BuildVisualNotes(item),
            Impressions = item.Insights?.Impressions,
            Reach = item.Insights?.Reach,
            Engagement = item.Insights?.Engagement,
            Saved = item.Insights?.Saved
        };
    }

    public static string BuildSummary(IReadOnlyList<ResearchSnapshotPost> posts, InstagramFetchStatus status)
    {
        if (posts.Count == 0)
        {
            return TrimRequired($"snapshot empty status={status}", ResearchArtifactLimits.SummaryMaxChars);
        }

        var sb = new StringBuilder();
        sb.Append($"posts={posts.Count}");
        var withInsights = posts.Count(p => p.Impressions is not null || p.Reach is not null);
        if (withInsights > 0)
        {
            sb.Append($" insights={withInsights}");
        }

        var top = posts
            .OrderByDescending(p => p.Engagement ?? p.Impressions ?? 0)
            .ThenByDescending(p => p.Timestamp ?? DateTimeOffset.MinValue)
            .Take(3);
        foreach (var p in top)
        {
            var bit = string.IsNullOrWhiteSpace(p.Caption)
                ? (p.VisualNotes ?? p.MediaType ?? p.MediaId)
                : p.Caption;
            sb.Append("; ");
            sb.Append(TrimRequired(bit, 80));
        }

        return TrimRequired(sb.ToString(), ResearchArtifactLimits.SummaryMaxChars);
    }

    private static string BuildVisualNotes(InstagramMediaItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.MediaType))
        {
            parts.Add(item.MediaType!);
        }

        if (item.Insights is not null)
        {
            if (item.Insights.Reach is long reach)
            {
                parts.Add($"reach={reach}");
            }

            if (item.Insights.Engagement is long eng)
            {
                parts.Add($"eng={eng}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(item.ThumbnailUrl) || !string.IsNullOrWhiteSpace(item.MediaUrl))
        {
            // Note presence of media without storing URL (SSRF/token hygiene for inject dumps).
            parts.Add("media-ref");
        }

        return TrimRequired(string.Join(' ', parts), ResearchArtifactLimits.VisualNotesMaxChars);
    }

    private static string TrimRequired(string value, int max)
    {
        var t = value.Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
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
