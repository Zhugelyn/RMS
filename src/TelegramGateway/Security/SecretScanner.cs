using System.Text.RegularExpressions;

namespace TelegramGateway.Security;

public static partial class SecretScanner
{
    private static readonly Regex CursorLikeKey = CursorLikeKeyRegex();

    public static bool ContainsForbiddenSecret(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains("CURSOR_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cursor api key", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("INSTAGRAM__ACCESSTOKEN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("INSTAGRAM_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("IG_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("VK__SERVICETOKEN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("VK_SERVICE_TOKEN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("VK_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("RAG__SERVICEKEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("RAG_SERVICE_KEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ELASTICSEARCH__PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ELASTIC_PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("MINIO__SECRETKEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("MINIO__ROOTPASSWORD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("MINIO_SECRET_KEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("api_key=", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("apikey=", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("access_token=", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("service_token=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Contains("IGQVJ", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("IGQWR", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CursorLikeKey.IsMatch(text) || InstagramLikeToken.IsMatch(text);
    }

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]{16,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CursorLikeKeyRegex();

    [GeneratedRegex(@"\bEAA[A-Za-z0-9]{20,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex InstagramLikeTokenRegex();

    private static readonly Regex InstagramLikeToken = InstagramLikeTokenRegex();
}
