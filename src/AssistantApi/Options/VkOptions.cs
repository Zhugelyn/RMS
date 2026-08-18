using System.ComponentModel.DataAnnotations;

namespace AssistantApi.Options;

public sealed class VkOptions
{
    public const string SectionName = "Vk";

    /// <summary>Optional plaintext bootstrap service token from env. Cleared after encrypt-at-rest seal.</summary>
    public string ServiceToken { get; set; } = string.Empty;

    /// <summary>Optional pre-encrypted blob (base64). Used when ServiceToken is empty.</summary>
    public string EncryptedServiceToken { get; set; } = string.Empty;

    /// <summary>
    /// Master key for AES-GCM seal (≥16 chars). Falls back to Cursor:MasterKey / Instagram:MasterKey when empty.
    /// Required when ServiceToken or EncryptedServiceToken is set.
    /// </summary>
    public string MasterKey { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.vk.com/method/";

    /// <summary>VK API version (official method params).</summary>
    public string ApiVersion { get; set; } = "5.199";

    /// <summary>wall.get count (clamped to <see cref="VkFetchLimits.MaxWallFetch"/>).</summary>
    [Range(1, VkFetchLimits.MaxWallFetch)]
    public int DefaultWallCount { get; set; } = VkFetchLimits.DefaultWallFetch;

    [Range(5, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Max bytes when downloading CDN photo URLs (SSRF-safe path; used later in media slice).</summary>
    [Range(64 * 1024, 20 * 1024 * 1024)]
    public int MaxMediaDownloadBytes { get; set; } = 5 * 1024 * 1024;
}

/// <summary>Hard limits for VK wall.get (align with InstagramFetchLimits).</summary>
public static class VkFetchLimits
{
    public const int DefaultWallFetch = 25;
    public const int MaxWallFetch = 50;
}
