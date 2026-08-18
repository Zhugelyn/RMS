using System.ComponentModel.DataAnnotations;

namespace AssistantApi.Options;

public sealed class InstagramOptions
{
    public const string SectionName = "Instagram";

    /// <summary>Optional plaintext bootstrap token from env. Cleared after encrypt-at-rest seal.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Optional pre-encrypted blob (base64). Used when AccessToken is empty.</summary>
    public string EncryptedAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Master key for AES-GCM seal (≥16 chars). Falls back to Cursor:MasterKey when empty.
    /// Required when AccessToken or EncryptedAccessToken is set.
    /// </summary>
    public string MasterKey { get; set; } = string.Empty;

    /// <summary>IG User ID (Instagram Login / Graph). Prefer over BusinessAccountId when both set.</summary>
    public string IgUserId { get; set; } = string.Empty;

    /// <summary>Instagram Business Account ID (Facebook Login path). Alias for IgUserId.</summary>
    public string BusinessAccountId { get; set; } = string.Empty;

    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com/v21.0/";

    /// <summary>Graph media page size (clamped to <see cref="InstagramFetchLimits.MaxMediaFetch"/>).</summary>
    [Range(1, InstagramFetchLimits.MaxMediaFetch)]
    public int DefaultMediaLimit { get; set; } = InstagramFetchLimits.DefaultMediaFetch;

    [Range(5, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Max bytes when downloading media_url (SSRF-safe path).</summary>
    [Range(64 * 1024, 20 * 1024 * 1024)]
    public int MaxMediaDownloadBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Resolved account id: IgUserId if set, else BusinessAccountId.</summary>
    public string ResolveAccountId() =>
        !string.IsNullOrWhiteSpace(IgUserId) ? IgUserId.Trim()
        : !string.IsNullOrWhiteSpace(BusinessAccountId) ? BusinessAccountId.Trim()
        : string.Empty;
}

/// <summary>Hard limits for Instagram Graph fetch (Phase 4 hardening).</summary>
public static class InstagramFetchLimits
{
    public const int DefaultMediaFetch = 25;
    public const int MaxMediaFetch = 50;
}
