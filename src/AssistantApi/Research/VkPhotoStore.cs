using System.Globalization;
using AssistantApi.Options;
using AssistantApi.Vk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantApi.Research;

/// <summary>
/// Downloads VK wall photo attachments into Research:ImageVolumePath (ADR-013 / phase6-vk-media).
/// Stores relative paths only — never persist CDN URLs as durable refs.
/// Soft-skip when volume missing; soft-fail per photo.
/// </summary>
public interface IVkPhotoStore
{
    bool IsConfigured { get; }

    Task<VkPhotoStoreResult> DownloadWallPhotosAsync(
        string runId,
        IReadOnlyList<VkWallPost> posts,
        CancellationToken cancellationToken = default);
}

public sealed class VkPhotoStoreResult
{
    public bool Skipped { get; init; }
    public string? SkipReason { get; init; }
    /// <summary>Relative to ImageVolumePath, e.g. research-media/vk-abc.</summary>
    public string RelativeRoot { get; init; } = string.Empty;
    /// <summary>Paths relative to RelativeRoot, e.g. vk/photo-01.jpg.</summary>
    public IReadOnlyList<string> RelativeFiles { get; init; } = Array.Empty<string>();
    /// <summary>Full relative MediaPath under volume (research-media/.../vk/photo-01.jpg).</summary>
    public IReadOnlyList<string> MediaPaths { get; init; } = Array.Empty<string>();
    public int Downloaded { get; init; }
    public int Attempted { get; init; }
}

public static class VkPhotoStoreLimits
{
    /// <summary>Cap photos downloaded per capture (align with plan gallery / GenerateImage).</summary>
    public const int MaxPhotos = ResearchImageLimits.MaxImages;

    public const string RelativePhotoDir = "vk";
}

public sealed class VkPhotoStore : IVkPhotoStore
{
    private readonly ResearchOptions _research;
    private readonly IVkMediaDownloader _downloader;
    private readonly ILogger<VkPhotoStore> _logger;

    public VkPhotoStore(
        IOptions<ResearchOptions> research,
        IVkMediaDownloader downloader,
        ILogger<VkPhotoStore> logger)
    {
        _research = research.Value;
        _downloader = downloader;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_research.ImageVolumePath);

    public async Task<VkPhotoStoreResult> DownloadWallPhotosAsync(
        string runId,
        IReadOnlyList<VkWallPost> posts,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new VkPhotoStoreResult
            {
                Skipped = true,
                SkipReason = "volume-missing"
            };
        }

        var volume = Path.GetFullPath(_research.ImageVolumePath);
        Directory.CreateDirectory(volume);

        var safeRun = ResearchGeneratedImageCollector.SanitizeRunId(
            string.IsNullOrWhiteSpace(runId) ? "vk-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) : "vk-" + runId);
        var relativeRoot = $"{ResearchImageLimits.RelativeMediaRoot}/{safeRun}";
        var cwd = Path.GetFullPath(Path.Combine(volume, ResearchImageLimits.RelativeMediaRoot, safeRun));
        if (!cwd.StartsWith(volume + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(cwd, volume, StringComparison.Ordinal))
        {
            _logger.LogWarning("VK photo store path escaped volume; skip");
            return new VkPhotoStoreResult
            {
                Skipped = true,
                SkipReason = "path-escape"
            };
        }

        var photoDir = Path.Combine(cwd, VkPhotoStoreLimits.RelativePhotoDir);
        Directory.CreateDirectory(photoDir);

        var candidates = CollectPhotoUrls(posts, VkPhotoStoreLimits.MaxPhotos);
        var relativeFiles = new List<string>();
        var mediaPaths = new List<string>();
        var index = 0;

        foreach (var url in candidates)
        {
            index++;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var dl = await _downloader.TryDownloadAsync(url, cancellationToken);
                if (!dl.Ok || dl.Bytes is null || dl.Bytes.Length == 0)
                {
                    _logger.LogDebug("VK photo download soft-fail index={Index} code={Code}", index, dl.ErrorCode);
                    continue;
                }

                if (dl.Bytes.Length > _research.MaxImageBytes)
                {
                    continue;
                }

                var ext = GuessExt(dl.ContentType, url);
                var name = $"photo-{index:00}{ext}";
                var abs = Path.Combine(photoDir, name);
                await File.WriteAllBytesAsync(abs, dl.Bytes, cancellationToken);

                var relFile = $"{VkPhotoStoreLimits.RelativePhotoDir}/{name}";
                var mediaPath = ResearchMediaPathGuard.SanitizeOrNull($"{relativeRoot}/{relFile}");
                if (mediaPath is null)
                {
                    continue;
                }

                relativeFiles.Add(relFile);
                mediaPaths.Add(mediaPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "VK photo download soft-fail index={Index}", index);
            }
        }

        return new VkPhotoStoreResult
        {
            RelativeRoot = relativeRoot,
            RelativeFiles = relativeFiles,
            MediaPaths = mediaPaths,
            Downloaded = relativeFiles.Count,
            Attempted = candidates.Count
        };
    }

    /// <summary>First allowlisted photo URL per post, then extras; cap total.</summary>
    public static IReadOnlyList<string> CollectPhotoUrls(IReadOnlyList<VkWallPost> posts, int max)
    {
        var urls = new List<string>(Math.Min(max, 16));
        foreach (var post in posts)
        {
            if (urls.Count >= max)
            {
                break;
            }

            foreach (var photo in post.Photos)
            {
                if (urls.Count >= max)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(photo.Url))
                {
                    continue;
                }

                if (!VkCdnUrlGuard.IsAllowedMediaUrl(photo.Url, out _, out _))
                {
                    continue;
                }

                urls.Add(photo.Url!);
            }
        }

        return urls;
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

        var path = url.Split('?', 2)[0];
        var ext = Path.GetExtension(path);
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            return ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ext.ToLowerInvariant();
        }

        return ".jpg";
    }
}

/// <summary>Used when DI wants a store but volume/downloader unused in unit tests.</summary>
public sealed class NoOpVkPhotoStore : IVkPhotoStore
{
    public bool IsConfigured => false;

    public Task<VkPhotoStoreResult> DownloadWallPhotosAsync(
        string runId,
        IReadOnlyList<VkWallPost> posts,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new VkPhotoStoreResult
        {
            Skipped = true,
            SkipReason = "noop"
        });
}
