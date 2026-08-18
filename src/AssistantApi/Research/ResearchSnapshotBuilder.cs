using System.Text;
using AssistantApi.Instagram;
using AssistantApi.Vk;

namespace AssistantApi.Research;

/// <summary>Maps Graph / VK media → normalized snapshot (no token, no raw media bytes).</summary>
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
            Summary = BuildSummary(posts, fetch.Status.ToString()),
            SourceStatus = fetch.Status.ToString(),
            Source = ResearchSources.Instagram
        };
    }

    /// <summary>
    /// Maps VK wall posts → snapshot with additive <c>source=vk</c>.
    /// Does not store CDN photo URLs (media download = later slice).
    /// </summary>
    public static ResearchSnapshot FromVkFetch(
        string userId,
        VkWallFetchResult fetch,
        DateTimeOffset? capturedAt = null)
    {
        var at = capturedAt ?? DateTimeOffset.UtcNow;
        var posts = fetch.Posts
            .Take(ResearchArtifactLimits.MaxPostsPerSnapshot)
            .Select(ToVkPost)
            .ToList();

        return new ResearchSnapshot
        {
            UserId = userId,
            CapturedAt = at,
            Posts = posts,
            PostCount = posts.Count,
            Summary = BuildVkSummary(posts, fetch),
            SourceStatus = fetch.Status.ToString(),
            Source = ResearchSources.Vk
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

    public static ResearchSnapshotPost ToVkPost(VkWallPost post)
    {
        var caption = Trim(post.Text, ResearchArtifactLimits.CaptionMaxChars);
        var mediaId = $"{post.OwnerId}_{post.Id}";
        var photoCount = post.Photos.Count;
        var mediaType = photoCount > 0 ? "PHOTO" : "TEXT";
        return new ResearchSnapshotPost
        {
            MediaId = mediaId,
            Caption = caption,
            MediaType = mediaType,
            Timestamp = post.Date,
            Permalink = $"https://vk.com/wall{post.OwnerId}_{post.Id}",
            VisualNotes = BuildVkVisualNotes(photoCount)
            // No CDN URLs — phase6-vk-media downloads later.
        };
    }

    public static string BuildSummary(IReadOnlyList<ResearchSnapshotPost> posts, InstagramFetchStatus status) =>
        BuildSummary(posts, status.ToString());

    public static string BuildSummary(IReadOnlyList<ResearchSnapshotPost> posts, string status)
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

    private static string BuildVkSummary(IReadOnlyList<ResearchSnapshotPost> posts, VkWallFetchResult fetch)
    {
        if (posts.Count == 0)
        {
            var empty =
                $"vk snapshot empty status={fetch.Status}" +
                (fetch.SkippedDonutCount > 0 ? $" donutSkipped={fetch.SkippedDonutCount}" : "");
            return TrimRequired(empty, ResearchArtifactLimits.SummaryMaxChars);
        }

        var sb = new StringBuilder();
        sb.Append("source=vk");
        if (!string.IsNullOrWhiteSpace(fetch.ScreenName))
        {
            sb.Append(' ');
            sb.Append(TrimRequired(fetch.ScreenName!, 40));
        }
        else if (fetch.OwnerId is long oid)
        {
            sb.Append($" owner={oid}");
        }

        sb.Append($" posts={posts.Count}");
        if (fetch.SkippedDonutCount > 0)
        {
            sb.Append($" donutSkipped={fetch.SkippedDonutCount}");
        }

        var top = posts
            .OrderByDescending(p => p.Timestamp ?? DateTimeOffset.MinValue)
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

    private static string BuildVkVisualNotes(int photoCount)
    {
        if (photoCount <= 0)
        {
            return "TEXT";
        }

        return TrimRequired($"PHOTO photos={photoCount}", ResearchArtifactLimits.VisualNotesMaxChars);
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
