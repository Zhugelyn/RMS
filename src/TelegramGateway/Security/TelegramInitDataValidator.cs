using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TelegramGateway.Security;

/// <summary>
/// Validates Telegram Mini App <c>initData</c> (HMAC-SHA256) and extracts user id.
/// See https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// </summary>
public static class TelegramInitDataValidator
{
    public const int DefaultMaxAgeSeconds = 86_400;

    public sealed record Result(bool Ok, long? UserId, string? ErrorCode);

    public static Result Validate(
        string? initData,
        string botToken,
        int maxAgeSeconds = DefaultMaxAgeSeconds,
        DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return new Result(false, null, "bot-token-missing");
        }

        if (string.IsNullOrWhiteSpace(initData))
        {
            return new Result(false, null, "init-data-missing");
        }

        // Cap raw size to blunt abuse (initData is typically <2KB).
        if (initData.Length > 4096)
        {
            return new Result(false, null, "init-data-too-large");
        }

        Dictionary<string, string> fields;
        try
        {
            fields = ParseQuery(initData);
        }
        catch
        {
            return new Result(false, null, "init-data-malformed");
        }

        if (!fields.TryGetValue("hash", out var hash) || string.IsNullOrWhiteSpace(hash))
        {
            return new Result(false, null, "hash-missing");
        }

        var dataCheckString = string.Join(
            '\n',
            fields
                .Where(kv => !string.Equals(kv.Key, "hash", StringComparison.Ordinal))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));

        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(botToken));
        var computed = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        var providedBytes = TryParseHex(hash);
        var computedBytes = TryParseHex(computedHex);
        if (providedBytes is null || computedBytes is null
            || providedBytes.Length != computedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(providedBytes, computedBytes))
        {
            return new Result(false, null, "hash-invalid");
        }

        if (!fields.TryGetValue("auth_date", out var authDateRaw)
            || !long.TryParse(authDateRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authUnix))
        {
            return new Result(false, null, "auth-date-missing");
        }

        var authAt = DateTimeOffset.FromUnixTimeSeconds(authUnix);
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var maxAge = TimeSpan.FromSeconds(Math.Clamp(maxAgeSeconds, 60, 7 * 86_400));
        if (authAt > now.AddMinutes(5) || now - authAt > maxAge)
        {
            return new Result(false, null, "auth-date-expired");
        }

        if (!fields.TryGetValue("user", out var userJson) || string.IsNullOrWhiteSpace(userJson))
        {
            return new Result(false, null, "user-missing");
        }

        long userId;
        try
        {
            using var doc = JsonDocument.Parse(userJson);
            if (!doc.RootElement.TryGetProperty("id", out var idEl)
                || !idEl.TryGetInt64(out userId)
                || userId <= 0)
            {
                return new Result(false, null, "user-id-invalid");
            }
        }
        catch
        {
            return new Result(false, null, "user-malformed");
        }

        return new Result(true, userId, null);
    }

    /// <summary>Builds a signed initData string for tests (not for production clients).</summary>
    public static string BuildSignedInitDataForTests(
        string botToken,
        long userId,
        DateTimeOffset? authDate = null,
        string? firstName = "Test")
    {
        var authUnix = (authDate ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var userJson = JsonSerializer.Serialize(new { id = userId, first_name = firstName ?? "Test" });
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authUnix.ToString(CultureInfo.InvariantCulture),
            ["query_id"] = "AAEAAAEAAAE",
            ["user"] = userJson
        };

        var dataCheckString = string.Join('\n', fields.Select(kv => $"{kv.Key}={kv.Value}"));
        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexString(
            HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLowerInvariant();

        var pairs = fields
            .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")
            .Append($"hash={hash}");
        return string.Join('&', pairs);
    }

    private static Dictionary<string, string> ParseQuery(string initData)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..eq]);
            var value = Uri.UnescapeDataString(part[(eq + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static byte[]? TryParseHex(string hex)
    {
        try
        {
            return Convert.FromHexString(hex);
        }
        catch
        {
            return null;
        }
    }
}
