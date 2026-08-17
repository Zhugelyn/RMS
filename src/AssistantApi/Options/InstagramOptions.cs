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

    [Range(1, 100)]
    public int DefaultMediaLimit { get; set; } = 25;

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
