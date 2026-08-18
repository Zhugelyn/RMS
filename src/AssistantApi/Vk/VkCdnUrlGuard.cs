using System.Net;

namespace AssistantApi.Vk;

/// <summary>
/// SSRF guard for VK CDN photo URLs (ADR-013).
/// Allowlist: *.userapi.com (and exact host userapi.com). No IP literals.
/// </summary>
public static class VkCdnUrlGuard
{
    private static readonly string[] AllowedSuffixes =
    [
        ".userapi.com"
    ];

    private static readonly string[] AllowedExactHosts =
    [
        "userapi.com"
    ];

    public static bool IsAllowedMediaUrl(string? url, out Uri? uri, out string? reason)
    {
        uri = null;
        reason = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            reason = "empty-url";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            reason = "not-absolute";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps)
        {
            reason = "scheme-not-https";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            reason = "userinfo-forbidden";
            return false;
        }

        var host = parsed.IdnHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            reason = "empty-host";
            return false;
        }

        if (IPAddress.TryParse(host, out _))
        {
            reason = "ip-literal-forbidden";
            return false;
        }

        if (!IsAllowedHost(host))
        {
            reason = "host-not-allowlisted";
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool IsAllowedHost(string host)
    {
        foreach (var exact in AllowedExactHosts)
        {
            if (host.Equals(exact, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var suffix in AllowedSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
