using System.Globalization;
using AssistantApi.Options;

namespace AssistantApi.Research;

/// <summary>Collect png|jpg|webp under a research-media run cwd (prefer out/). Cap ≤14.</summary>
public static class ResearchGeneratedImageCollector
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    public static IReadOnlyList<string> Collect(
        string cwd,
        int cap = ResearchImageLimits.MaxImages,
        DateTimeOffset? modifiedSince = null)
    {
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            return Array.Empty<string>();
        }

        var root = Path.GetFullPath(cwd);
        var limit = Math.Clamp(cap, 0, ResearchImageLimits.MaxImages);
        if (limit == 0)
        {
            return Array.Empty<string>();
        }

        var since = modifiedSince?.UtcDateTime ?? DateTime.MinValue;
        var found = new List<(string Rel, DateTime Mtime)>();

        var outDir = Path.Combine(root, "out");
        if (Directory.Exists(outDir))
        {
            CollectUnder(root, outDir, since, found, skipOutPrefix: false);
        }

        // Prefer out/ when it has images; only scan cwd root as fallback.
        if (found.Count == 0)
        {
            CollectUnder(root, root, since, found, skipOutPrefix: true);
        }

        var ordered = found
            .GroupBy(x => x.Rel, StringComparer.Ordinal)
            .Select(g => g.OrderBy(x => x.Mtime).First())
            .OrderBy(x => x.Mtime)
            .ThenBy(x => x.Rel, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => x.Rel)
            .ToList();

        return ordered;
    }

    private static void CollectUnder(
        string root,
        string scanRoot,
        DateTime sinceUtc,
        List<(string Rel, DateTime Mtime)> found,
        bool skipOutPrefix)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(scanRoot, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        foreach (var abs in files)
        {
            var ext = Path.GetExtension(abs);
            if (!Exts.Contains(ext))
            {
                continue;
            }

            string rel;
            try
            {
                rel = Path.GetRelativePath(root, abs).Replace('\\', '/');
            }
            catch
            {
                continue;
            }

            if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            {
                continue;
            }

            var top = rel.Split('/')[0];
            if (string.Equals(top, "refs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (skipOutPrefix && string.Equals(top, "out", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateTime mtime;
            try
            {
                mtime = File.GetLastWriteTimeUtc(abs);
            }
            catch
            {
                continue;
            }

            if (mtime + TimeSpan.FromSeconds(1) < sinceUtc)
            {
                continue;
            }

            found.Add((rel, mtime));
        }
    }

    /// <summary>Map collected relative paths onto plan items (by index). Cap = min(items, images, 14).</summary>
    public static ResearchPlan ApplyMediaPaths(ResearchPlan plan, IReadOnlyList<string> relativeImages, string runRelativeRoot)
    {
        var items = plan.Items.ToList();
        var n = Math.Min(Math.Min(items.Count, relativeImages.Count), ResearchImageLimits.MaxImages);
        for (var i = 0; i < n; i++)
        {
            var media = string.IsNullOrWhiteSpace(runRelativeRoot)
                ? relativeImages[i]
                : $"{runRelativeRoot.TrimEnd('/')}/{relativeImages[i].TrimStart('/')}";
            media = ResearchMediaPathGuard.SanitizeOrNull(media);
            if (media is null)
            {
                continue;
            }

            var prev = items[i];
            items[i] = new ResearchPlanItem
            {
                Date = prev.Date,
                Caption = prev.Caption,
                Hashtags = prev.Hashtags,
                ImagePrompt = prev.ImagePrompt,
                MediaPath = media,
                TelegramFileId = prev.TelegramFileId,
                Status = prev.Status == ResearchPlanItemStatus.Draft
                    ? ResearchPlanItemStatus.Ready
                    : prev.Status
            };
        }

        return new ResearchPlan
        {
            Id = plan.Id,
            UserId = plan.UserId,
            CreatedAt = plan.CreatedAt,
            WindowStart = plan.WindowStart,
            WindowEnd = plan.WindowEnd,
            Items = items
        };
    }

    public static string SanitizeRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        var chars = runId.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .Take(64)
            .ToArray();
        var s = new string(chars).Trim('-');
        return string.IsNullOrEmpty(s) ? "run" : s;
    }
}
