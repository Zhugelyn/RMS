using System.Globalization;
using System.Text.Json;

namespace AssistantApi.Instagram;

/// <summary>Maps Graph API JSON fixtures/responses → domain models. Pure; no network.</summary>
public static class InstagramMediaMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<InstagramMediaItem> MapMediaList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return MapMediaList(doc.RootElement);
    }

    public static IReadOnlyList<InstagramMediaItem> MapMediaList(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<InstagramMediaItem>();
        }

        var list = new List<InstagramMediaItem>(data.GetArrayLength());
        foreach (var item in data.EnumerateArray())
        {
            var mapped = MapMediaItem(item);
            if (mapped is not null)
            {
                list.Add(mapped);
            }
        }

        return list;
    }

    public static InstagramMediaItem? MapMediaItem(JsonElement item)
    {
        if (!item.TryGetProperty("id", out var idEl))
        {
            return null;
        }

        var id = idEl.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return new InstagramMediaItem
        {
            Id = id,
            Caption = GetString(item, "caption"),
            MediaUrl = GetString(item, "media_url"),
            Permalink = GetString(item, "permalink"),
            MediaType = GetString(item, "media_type"),
            ThumbnailUrl = GetString(item, "thumbnail_url"),
            Timestamp = ParseTimestamp(GetString(item, "timestamp"))
        };
    }

    public static InstagramMediaInsights MapInsights(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return MapInsights(doc.RootElement);
    }

    public static InstagramMediaInsights MapInsights(JsonElement root)
    {
        var raw = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var metric in data.EnumerateArray())
            {
                var name = GetString(metric, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!TryReadMetricValue(metric, out var value))
                {
                    continue;
                }

                raw[name] = value;
            }
        }

        return new InstagramMediaInsights
        {
            Impressions = GetRaw(raw, "impressions"),
            Reach = GetRaw(raw, "reach"),
            Engagement = GetRaw(raw, "engagement") ?? GetRaw(raw, "total_interactions"),
            Saved = GetRaw(raw, "saved"),
            VideoViews = GetRaw(raw, "video_views") ?? GetRaw(raw, "total_views"),
            Raw = raw
        };
    }

    public static InstagramMediaItem WithInsights(InstagramMediaItem item, InstagramMediaInsights insights) =>
        new()
        {
            Id = item.Id,
            Caption = item.Caption,
            MediaUrl = item.MediaUrl,
            Timestamp = item.Timestamp,
            Permalink = item.Permalink,
            MediaType = item.MediaType,
            ThumbnailUrl = item.ThumbnailUrl,
            Insights = insights
        };

    private static long? GetRaw(IReadOnlyDictionary<string, long> raw, string key) =>
        raw.TryGetValue(key, out var v) ? v : null;

    private static bool TryReadMetricValue(JsonElement metric, out long value)
    {
        value = 0;
        if (metric.TryGetProperty("values", out var values) &&
            values.ValueKind == JsonValueKind.Array &&
            values.GetArrayLength() > 0)
        {
            var first = values[0];
            if (first.TryGetProperty("value", out var v) && v.TryGetInt64(out value))
            {
                return true;
            }
        }

        // Some Graph payloads put value at top level.
        if (metric.TryGetProperty("value", out var top) && top.TryGetInt64(out value))
        {
            return true;
        }

        return false;
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static DateTimeOffset? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto;
        }

        return null;
    }
}
