using System.Text.Json;
using System.Text.RegularExpressions;

namespace AssistantApi.Research;

/// <summary>Open VK community target for research allowlist (ADR-013). No tokens.</summary>
public sealed class VkCommunityTarget
{
    /// <summary>Public screen_name without @ (e.g. babor_bryansk).</summary>
    public string? ScreenName { get; init; }

    /// <summary>wall.get owner_id (negative for groups/pages).</summary>
    public long? OwnerId { get; init; }

    public string Display =>
        !string.IsNullOrWhiteSpace(ScreenName) ? ScreenName!
        : OwnerId is long id ? id.ToString()
        : "?";
}

public static partial class VkCommunityAllowlist
{
    public const int MaxCommunities = 10;
    public const int MaxScreenNameLength = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static IReadOnlyList<VkCommunityTarget> Normalize(IEnumerable<VkCommunityTarget>? raw)
    {
        if (raw is null)
        {
            return Array.Empty<VkCommunityTarget>();
        }

        var result = new List<VkCommunityTarget>(MaxCommunities);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenOwners = new HashSet<long>();

        foreach (var item in raw)
        {
            if (result.Count >= MaxCommunities)
            {
                break;
            }

            var normalized = NormalizeOne(item);
            if (normalized is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalized.ScreenName))
            {
                if (!seenNames.Add(normalized.ScreenName!))
                {
                    continue;
                }
            }

            if (normalized.OwnerId is long oid)
            {
                if (!seenOwners.Add(oid))
                {
                    continue;
                }
            }

            result.Add(normalized);
        }

        return result;
    }

    /// <summary>
    /// Parse user input: screen_name, club123 / public123, numeric owner_id, vk.com/… URLs.
    /// Rejects secret-looking fragments.
    /// </summary>
    public static VkCommunityTarget ParseOne(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ResearchValidationException("VK community is empty.");
        }

        var text = raw.Trim();
        if (LooksLikeSecret(text))
        {
            throw new ResearchValidationException(
                "VK service token must not be sent here. Only screen_name or owner_id.");
        }

        // Strip common URL prefixes.
        text = text
            .Replace("https://vk.com/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://vk.com/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://m.vk.com/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://m.vk.com/", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimStart('@')
            .Trim('/');

        if (text.Contains('?', StringComparison.Ordinal))
        {
            text = text.Split('?', 2)[0];
        }

        if (LooksLikeSecret(text))
        {
            throw new ResearchValidationException(
                "VK service token must not be sent here. Only screen_name or owner_id.");
        }

        // club123 / public123 → owner_id = -123
        var club = ClubIdRegex().Match(text);
        if (club.Success && long.TryParse(club.Groups[1].Value, out var clubId) && clubId > 0)
        {
            return new VkCommunityTarget { OwnerId = -clubId };
        }

        if (long.TryParse(text, out var ownerId) && ownerId != 0)
        {
            return new VkCommunityTarget { OwnerId = ownerId };
        }

        var screen = NormalizeScreenName(text);
        if (screen is null)
        {
            throw new ResearchValidationException(
                "Invalid VK community. Use screen_name (e.g. babor_bryansk) or owner_id.");
        }

        return new VkCommunityTarget { ScreenName = screen };
    }

    public static IReadOnlyList<VkCommunityTarget> ParseMany(IEnumerable<string>? lines)
    {
        if (lines is null)
        {
            return Array.Empty<VkCommunityTarget>();
        }

        var parsed = new List<VkCommunityTarget>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            foreach (var part in line.Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                parsed.Add(ParseOne(part));
            }
        }

        return Normalize(parsed);
    }

    public static string Serialize(IReadOnlyList<VkCommunityTarget> communities) =>
        JsonSerializer.Serialize(Normalize(communities), JsonOptions);

    public static IReadOnlyList<VkCommunityTarget> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<VkCommunityTarget>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<VkCommunityTarget>>(json, JsonOptions);
            return Normalize(list);
        }
        catch (JsonException)
        {
            return Array.Empty<VkCommunityTarget>();
        }
    }

    public static bool SameTarget(VkCommunityTarget a, VkCommunityTarget b)
    {
        if (a.OwnerId is long ao && b.OwnerId is long bo && ao == bo)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(a.ScreenName) && !string.IsNullOrWhiteSpace(b.ScreenName)
            && string.Equals(a.ScreenName, b.ScreenName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static IReadOnlyList<VkCommunityTarget> Add(
        IReadOnlyList<VkCommunityTarget> existing,
        VkCommunityTarget target)
    {
        var list = existing.ToList();
        if (list.Any(x => SameTarget(x, target)))
        {
            return Normalize(list);
        }

        if (list.Count >= MaxCommunities)
        {
            throw new ResearchValidationException($"VK allowlist cap is {MaxCommunities} communities.");
        }

        list.Add(target);
        return Normalize(list);
    }

    public static IReadOnlyList<VkCommunityTarget> Remove(
        IReadOnlyList<VkCommunityTarget> existing,
        VkCommunityTarget target) =>
        Normalize(existing.Where(x => !SameTarget(x, target)));

    private static VkCommunityTarget? NormalizeOne(VkCommunityTarget item)
    {
        string? screen = null;
        long? owner = null;

        if (!string.IsNullOrWhiteSpace(item.ScreenName))
        {
            screen = NormalizeScreenName(item.ScreenName);
            if (screen is null)
            {
                return null;
            }

            if (LooksLikeSecret(screen))
            {
                throw new ResearchValidationException(
                    "VK service token must not be sent here. Only screen_name or owner_id.");
            }
        }

        if (item.OwnerId is long oid && oid != 0)
        {
            owner = oid;
        }

        if (screen is null && owner is null)
        {
            return null;
        }

        return new VkCommunityTarget { ScreenName = screen, OwnerId = owner };
    }

    private static string? NormalizeScreenName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim().TrimStart('@');
        if (s.Length > MaxScreenNameLength)
        {
            s = s[..MaxScreenNameLength];
        }

        if (!ScreenNameRegex().IsMatch(s))
        {
            return null;
        }

        return s;
    }

    private static bool LooksLikeSecret(string text) =>
        text.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("service_token", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("VK__SERVICETOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("VK_SERVICE_TOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("VK_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("IGQVJ", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("EAA", StringComparison.Ordinal) ||
        text.Contains("sk-", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?:club|public|event)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClubIdRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._]{2,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScreenNameRegex();
}
