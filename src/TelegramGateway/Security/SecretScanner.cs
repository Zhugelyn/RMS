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
            text.Contains("api_key=", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("apikey=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CursorLikeKey.IsMatch(text);
    }

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]{16,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CursorLikeKeyRegex();
}
