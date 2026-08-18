using System.Globalization;
using System.Text.Json;

namespace AssistantApi.Vk;

/// <summary>Maps VK API JSON fixtures/responses → domain models. Pure; no network.</summary>
public static class VkWallMapper
{
    public static VkResolveResult MapResolve(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return MapResolve(doc.RootElement);
    }

    public static VkResolveResult MapResolve(JsonElement root)
    {
        if (TryMapApiError(root, out var errStatus, out var code, out var message))
        {
            return VkResolveResult.Soft(errStatus, code, message);
        }

        if (!root.TryGetProperty("response", out var response))
        {
            return VkResolveResult.Soft(
                VkFetchStatus.SkippedUnresolved,
                "vk-resolve-empty",
                "VK screen name could not be resolved.");
        }

        // VK returns response: [] when screen name is unknown.
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.False ||
            (response.ValueKind == JsonValueKind.Array && response.GetArrayLength() == 0))
        {
            return VkResolveResult.Soft(
                VkFetchStatus.SkippedUnresolved,
                "vk-resolve-empty",
                "VK screen name could not be resolved.");
        }

        if (response.ValueKind != JsonValueKind.Object)
        {
            return VkResolveResult.Soft(
                VkFetchStatus.SoftError,
                "vk-resolve-parse",
                "VK resolve response could not be parsed.");
        }

        var type = GetString(response, "type");
        if (!response.TryGetProperty("object_id", out var idEl) || !idEl.TryGetInt64(out var objectId))
        {
            return VkResolveResult.Soft(
                VkFetchStatus.SkippedUnresolved,
                "vk-resolve-empty",
                "VK screen name could not be resolved.");
        }

        long? ownerId = type switch
        {
            "group" or "page" or "event" => -objectId,
            "user" => objectId,
            _ => null
        };

        if (ownerId is null)
        {
            return VkResolveResult.Soft(
                VkFetchStatus.SkippedUnresolved,
                "vk-resolve-unsupported-type",
                "VK screen name resolved to an unsupported object type.");
        }

        return new VkResolveResult
        {
            Status = VkFetchStatus.Ok,
            ObjectType = type,
            ObjectId = objectId,
            OwnerId = ownerId
        };
    }

    public static IReadOnlyList<VkWallPost> MapWallItems(string json, out int skippedDonut)
    {
        using var doc = JsonDocument.Parse(json);
        return MapWallItems(doc.RootElement, out skippedDonut);
    }

    public static IReadOnlyList<VkWallPost> MapWallItems(JsonElement root, out int skippedDonut)
    {
        skippedDonut = 0;
        if (!root.TryGetProperty("response", out var response))
        {
            return Array.Empty<VkWallPost>();
        }

        JsonElement items;
        if (response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("items", out var itemsEl) &&
            itemsEl.ValueKind == JsonValueKind.Array)
        {
            items = itemsEl;
        }
        else if (response.ValueKind == JsonValueKind.Array)
        {
            // Legacy: first element is count.
            if (response.GetArrayLength() <= 1)
            {
                return Array.Empty<VkWallPost>();
            }

            items = response;
        }
        else
        {
            return Array.Empty<VkWallPost>();
        }

        var list = new List<VkWallPost>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Skip bare count integer in legacy array form.
            if (!item.TryGetProperty("id", out _))
            {
                continue;
            }

            var mapped = MapWallPost(item);
            if (mapped is null)
            {
                continue;
            }

            if (mapped.IsDonut)
            {
                skippedDonut++;
                continue;
            }

            list.Add(mapped);
        }

        return list;
    }

    public static VkWallPost? MapWallPost(JsonElement item)
    {
        if (!item.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
        {
            return null;
        }

        long ownerId = 0;
        if (item.TryGetProperty("owner_id", out var ownerEl))
        {
            ownerEl.TryGetInt64(out ownerId);
        }

        var isDonut = IsDonutPost(item);
        var photos = MapPhotoAttachments(item);

        return new VkWallPost
        {
            Id = id,
            OwnerId = ownerId,
            Text = GetString(item, "text"),
            Date = ParseUnixDate(item),
            IsDonut = isDonut,
            Photos = photos
        };
    }

    public static bool TryMapApiError(
        JsonElement root,
        out VkFetchStatus status,
        out string code,
        out string message)
    {
        status = VkFetchStatus.SoftError;
        code = "vk-api-error";
        message = "VK API request failed.";

        if (!root.TryGetProperty("error", out var err) || err.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var apiCode = 0;
        if (err.TryGetProperty("error_code", out var codeEl))
        {
            codeEl.TryGetInt32(out apiCode);
        }

        // Never echo error_msg — may contain token fragments.
        (status, code, message) = ClassifyVkErrorCode(apiCode);
        return true;
    }

    public static (VkFetchStatus Status, string Code, string Message) ClassifyVkErrorCode(int apiCode) =>
        apiCode switch
        {
            5 or 1116 => (VkFetchStatus.TokenInvalid, "vk-token-invalid",
                "VK service token is invalid or expired."),
            6 or 9 or 29 => (VkFetchStatus.RateLimited, "vk-rate-limited",
                "VK API rate limit reached. Try again later."),
            15 or 18 or 30 or 200 or 203 or 212 or 221 => (VkFetchStatus.SoftSkippedClosed,
                "vk-wall-closed",
                "VK wall is closed, private, or otherwise inaccessible."),
            _ => (VkFetchStatus.SoftError, "vk-api-error", "VK API request failed.")
        };

    private static bool IsDonutPost(JsonElement item)
    {
        if (item.TryGetProperty("is_donut", out var flag) &&
            flag.ValueKind is JsonValueKind.True)
        {
            return true;
        }

        if (item.TryGetProperty("donut", out var donut) &&
            donut.ValueKind == JsonValueKind.Object &&
            donut.TryGetProperty("is_donut", out var nested) &&
            nested.ValueKind is JsonValueKind.True)
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<VkPhotoAttachment> MapPhotoAttachments(JsonElement item)
    {
        if (!item.TryGetProperty("attachments", out var attachments) ||
            attachments.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<VkPhotoAttachment>();
        }

        var list = new List<VkPhotoAttachment>();
        foreach (var att in attachments.EnumerateArray())
        {
            var type = GetString(att, "type");
            if (!string.Equals(type, "photo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!att.TryGetProperty("photo", out var photo) || photo.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            long photoId = 0;
            long ownerId = 0;
            if (photo.TryGetProperty("id", out var pid))
            {
                pid.TryGetInt64(out photoId);
            }

            if (photo.TryGetProperty("owner_id", out var oid))
            {
                oid.TryGetInt64(out ownerId);
            }

            PickLargestAllowedSize(photo, out var url, out var width, out var height);
            list.Add(new VkPhotoAttachment
            {
                PhotoId = photoId,
                OwnerId = ownerId,
                Url = url,
                Width = width,
                Height = height
            });
        }

        return list;
    }

    private static void PickLargestAllowedSize(
        JsonElement photo,
        out string? url,
        out int? width,
        out int? height)
    {
        url = null;
        width = null;
        height = null;
        if (!photo.TryGetProperty("sizes", out var sizes) || sizes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var bestArea = -1;
        foreach (var size in sizes.EnumerateArray())
        {
            var candidate = GetString(size, "url");
            if (!VkCdnUrlGuard.IsAllowedMediaUrl(candidate, out _, out _))
            {
                continue;
            }

            var w = size.TryGetProperty("width", out var wEl) && wEl.TryGetInt32(out var wi) ? wi : 0;
            var h = size.TryGetProperty("height", out var hEl) && hEl.TryGetInt32(out var hi) ? hi : 0;
            var area = w * h;
            if (area >= bestArea)
            {
                bestArea = area;
                url = candidate;
                width = w > 0 ? w : null;
                height = h > 0 ? h : null;
            }
        }
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static DateTimeOffset? ParseUnixDate(JsonElement item)
    {
        if (!item.TryGetProperty("date", out var dateEl))
        {
            return null;
        }

        if (dateEl.TryGetInt64(out var unix))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        if (dateEl.ValueKind == JsonValueKind.String &&
            long.TryParse(dateEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out unix))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        return null;
    }
}
