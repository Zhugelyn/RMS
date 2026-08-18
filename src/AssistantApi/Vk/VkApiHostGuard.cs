using System.Net;

namespace AssistantApi.Vk;

/// <summary>
/// Restricts VK HTTP API base URL to official api.vk.com (ADR-013).
/// Blocks scrape hosts (m.vk.com), OAuth hosts, IP literals, non-HTTPS.
/// </summary>
public static class VkApiHostGuard
{
    public const string AllowedHost = "api.vk.com";
    public const string DefaultBaseUrl = "https://api.vk.com/method/";

    public static bool IsAllowedApiBaseUrl(string? url, out Uri? uri, out string? reason)
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

        if (!host.Equals(AllowedHost, StringComparison.OrdinalIgnoreCase))
        {
            reason = "host-not-api-vk";
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>Normalize to trailing-slash method base; falls back to default when invalid/empty.</summary>
    public static string NormalizeOrDefault(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultBaseUrl;
        }

        if (!IsAllowedApiBaseUrl(configured, out var uri, out _))
        {
            return DefaultBaseUrl;
        }

        var path = uri!.AbsolutePath;
        if (!path.EndsWith("/", StringComparison.Ordinal))
        {
            path += "/";
        }

        return $"{uri.Scheme}://{uri.Host}{path}";
    }
}
