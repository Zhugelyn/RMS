using AssistantApi.Instagram;
using AssistantApi.Options;
using AssistantApi.Packs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantApi.Research;

/// <summary>Prepares volume/research-media/&lt;runId&gt; with marketing pack + IG refs.</summary>
public interface IResearchImageWorkspace
{
    bool IsConfigured { get; }

    /// <summary>Absolute cwd for bridge local Agent.create. Null if volume not configured.</summary>
    Task<ResearchImageWorkspaceInfo?> PrepareAsync(
        string runId,
        ResearchPlan plan,
        IReadOnlyList<InstagramMediaItem> sourceMedia,
        CancellationToken cancellationToken);
}

public sealed class ResearchImageWorkspaceInfo
{
    public string AbsoluteCwd { get; init; } = string.Empty;
    /// <summary>Relative to ImageVolumePath, e.g. research-media/abc123</summary>
    public string RelativeRoot { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public int RefsDownloaded { get; init; }
}

public sealed class ResearchImageWorkspace : IResearchImageWorkspace
{
    private readonly ResearchOptions _options;
    private readonly IInstagramMediaDownloader _downloader;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ResearchImageWorkspace> _logger;

    public ResearchImageWorkspace(
        IOptions<ResearchOptions> options,
        IInstagramMediaDownloader downloader,
        IHostEnvironment env,
        ILogger<ResearchImageWorkspace> logger)
    {
        _options = options.Value;
        _downloader = downloader;
        _env = env;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ImageVolumePath)
        && Directory.Exists(Path.GetFullPath(_options.ImageVolumePath));

    public async Task<ResearchImageWorkspaceInfo?> PrepareAsync(
        string runId,
        ResearchPlan plan,
        IReadOnlyList<InstagramMediaItem> sourceMedia,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ImageVolumePath))
        {
            return null;
        }

        var volume = Path.GetFullPath(_options.ImageVolumePath);
        Directory.CreateDirectory(volume);

        var safeRun = ResearchGeneratedImageCollector.SanitizeRunId(runId);
        var relativeRoot = $"{ResearchImageLimits.RelativeMediaRoot}/{safeRun}";
        var cwd = Path.GetFullPath(Path.Combine(volume, ResearchImageLimits.RelativeMediaRoot, safeRun));
        if (!cwd.StartsWith(volume + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(cwd, volume, StringComparison.Ordinal))
        {
            _logger.LogWarning("Research image workspace path escaped volume; skip");
            return null;
        }

        Directory.CreateDirectory(cwd);
        Directory.CreateDirectory(Path.Combine(cwd, "out"));
        Directory.CreateDirectory(Path.Combine(cwd, "refs"));

        CopyMarketingPackSkeleton(cwd);

        var refs = 0;
        var maxRefs = Math.Min(sourceMedia.Count, ResearchImageLimits.MaxImages);
        for (var i = 0; i < maxRefs; i++)
        {
            var item = sourceMedia[i];
            var url = !string.IsNullOrWhiteSpace(item.MediaUrl) ? item.MediaUrl : item.ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            try
            {
                var dl = await _downloader.TryDownloadAsync(url!, cancellationToken);
                if (!dl.Ok || dl.Bytes is null || dl.Bytes.Length == 0)
                {
                    continue;
                }

                if (dl.Bytes.Length > _options.MaxImageBytes)
                {
                    continue;
                }

                var ext = GuessExt(dl.ContentType, url!);
                var name = $"ref-{i + 1:00}{ext}";
                var path = Path.Combine(cwd, "refs", name);
                await File.WriteAllBytesAsync(path, dl.Bytes, cancellationToken);
                refs++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Research image ref download soft-fail index={Index}", i);
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(cwd, "plan-prompts.md"),
            BuildPromptsMarkdown(plan),
            cancellationToken);

        return new ResearchImageWorkspaceInfo
        {
            AbsoluteCwd = cwd,
            RelativeRoot = relativeRoot,
            RunId = safeRun,
            RefsDownloaded = refs
        };
    }

    private void CopyMarketingPackSkeleton(string cwd)
    {
        var packRoot = ResolveMarketingPackRoot();
        if (packRoot is null)
        {
            _logger.LogWarning("Marketing pack root not found; research image cwd will lack AGENTS/skills");
            WriteMinimalAgents(cwd);
            return;
        }

        CopyFileIfExists(Path.Combine(packRoot, "AGENTS.md"), Path.Combine(cwd, "AGENTS.md"));
        CopyDirectoryIfExists(
            Path.Combine(packRoot, ".cursor"),
            Path.Combine(cwd, ".cursor"));
        // Ensure generate-research-images skill is present even if .cursor copy missed it.
        EnsureGenerateSkill(cwd, packRoot);
    }

    private string? ResolveMarketingPackRoot()
    {
        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, "AgentPacks", PackIds.Marketing),
            Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "AgentPacks", PackIds.Marketing)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "AgentPacks", PackIds.Marketing))
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "AGENTS.md")))
            {
                return c;
            }
        }

        return null;
    }

    private static void EnsureGenerateSkill(string cwd, string packRoot)
    {
        var src = Path.Combine(packRoot, ".cursor", "skills", "generate-research-images", "SKILL.md");
        var alt = Path.Combine(packRoot, "skills", "generate-research-images", "SKILL.md");
        var destDir = Path.Combine(cwd, ".cursor", "skills", "generate-research-images");
        Directory.CreateDirectory(destDir);
        if (File.Exists(src))
        {
            File.Copy(src, Path.Combine(destDir, "SKILL.md"), overwrite: true);
        }
        else if (File.Exists(alt))
        {
            File.Copy(alt, Path.Combine(destDir, "SKILL.md"), overwrite: true);
        }
    }

    private static void WriteMinimalAgents(string cwd)
    {
        File.WriteAllText(
            Path.Combine(cwd, "AGENTS.md"),
            "# Marketing research images\n\nUse GenerateImage skill. Write ≤14 images into out/.\n");
    }

    private static void CopyFileIfExists(string src, string dest)
    {
        if (File.Exists(src))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
        }
    }

    private static void CopyDirectoryIfExists(string src, string dest)
    {
        if (!Directory.Exists(src))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string BuildPromptsMarkdown(ResearchPlan plan)
    {
        var lines = new List<string>
        {
            "# Research image prompts",
            "",
            "Use Cursor **GenerateImage** only (not OpenAI Images / DALL·E).",
            $"Create at most {ResearchImageLimits.MaxImages} images under `out/` as day-01.png … day-14.png.",
            "Reference photos (if any) are in `refs/`.",
            ""
        };

        var i = 1;
        foreach (var item in plan.Items.Take(ResearchImageLimits.MaxImages))
        {
            lines.Add($"## Day {i:00} ({item.Date:yyyy-MM-dd})");
            lines.Add(item.ImagePrompt);
            lines.Add("");
            i++;
        }

        return string.Join('\n', lines);
    }

    private static string GuessExt(string? contentType, string url)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase)) return ".png";
            if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
            if (contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("jpg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
        }

        var ext = Path.GetExtension(url.Split('?', 2)[0]);
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            return ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ext.ToLowerInvariant();
        }

        return ".jpg";
    }
}
