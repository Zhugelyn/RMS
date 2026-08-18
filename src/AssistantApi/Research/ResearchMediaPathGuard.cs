namespace AssistantApi.Research;

/// <summary>
/// Plan MediaPath must be a relative volume path — never a durable CDN/HTTP URL (Phase 6 hardening).
/// </summary>
public static class ResearchMediaPathGuard
{
    public static bool IsSafeRelativeMediaPath(string? path, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "empty";
            return false;
        }

        var p = path.Replace('\\', '/').Trim();
        if (p.Contains("://", StringComparison.Ordinal)
            || p.StartsWith("//", StringComparison.Ordinal))
        {
            reason = "absolute-url-forbidden";
            return false;
        }

        if (p.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
        {
            reason = "path-traversal";
            return false;
        }

        if (p.Contains("userapi.com", StringComparison.OrdinalIgnoreCase)
            || p.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || p.Contains("VK__", StringComparison.OrdinalIgnoreCase))
        {
            reason = "secret-or-cdn-forbidden";
            return false;
        }

        return true;
    }

    public static string? SanitizeOrNull(string? path) =>
        IsSafeRelativeMediaPath(path, out _) ? path!.Replace('\\', '/').Trim().TrimStart('/') : null;
}
