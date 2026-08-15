using System.ComponentModel.DataAnnotations;

namespace AssistantApi.Options;

public sealed class CursorOptions
{
    public const string SectionName = "Cursor";

    /// <summary>Optional plaintext bootstrap key from env. Cleared from process memory after encrypt-at-rest seal.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional pre-encrypted blob (base64). Used when ApiKey is empty.</summary>
    public string EncryptedApiKey { get; set; } = string.Empty;

    /// <summary>32-byte master key as base64, or any passphrase (≥16 chars) that is hashed to 32 bytes.</summary>
    [MinLength(16)]
    public string MasterKey { get; set; } = string.Empty;

    /// <summary>Internal cursor-sdk-bridge base URL.</summary>
    public string BridgeBaseUrl { get; set; } = "http://cursor-sdk-bridge:8090";

    public string Model { get; set; } = "composer-2.5";

    public bool PreferCursorWhenKeyPresent { get; set; } = true;

    [Range(5, 600)]
    public int RequestTimeoutSeconds { get; set; } = 120;
}
