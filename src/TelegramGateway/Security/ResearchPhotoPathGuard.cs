using System.Text;

namespace TelegramGateway.Security;

/// <summary>Resolves research photo paths under ImageVolumePath with traversal guard.</summary>
public static class ResearchPhotoPathGuard
{
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    public static bool TryResolve(
        string? imageVolumePath,
        string relativePath,
        int maxBytes,
        out string absolutePath,
        out string? error)
    {
        absolutePath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(imageVolumePath))
        {
            error = "volume-missing";
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "path-empty";
            return false;
        }

        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        if (rel.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            error = "path-traversal";
            return false;
        }

        var ext = Path.GetExtension(rel);
        if (!AllowedExt.Contains(ext))
        {
            error = "ext-not-allowed";
            return false;
        }

        var volume = Path.GetFullPath(imageVolumePath);
        var combined = Path.GetFullPath(Path.Combine(volume, rel));
        if (!combined.StartsWith(volume + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(combined, volume, StringComparison.Ordinal))
        {
            error = "path-escape";
            return false;
        }

        if (!File.Exists(combined))
        {
            error = "not-found";
            return false;
        }

        var len = new FileInfo(combined).Length;
        if (len <= 0 || len > maxBytes)
        {
            error = "size";
            return false;
        }

        absolutePath = combined;
        return true;
    }
}
