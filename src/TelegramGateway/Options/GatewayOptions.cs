using System.ComponentModel.DataAnnotations;

namespace TelegramGateway.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    [Required]
    [MinLength(10)]
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Optional Telegram webhook secret token header value.</summary>
    public string? WebhookSecretToken { get; set; }

    /// <summary>When true, gateway starts long polling (local/dev).</summary>
    public bool UsePolling { get; set; } = true;

    /// <summary>
    /// Max age for Mini App initData auth_date (seconds). Mutations reject stale HMAC payloads.
    /// Env: TELEGRAM__INITDATAMAXAGESECONDS
    /// </summary>
    [Range(60, 604_800)]
    public int InitDataMaxAgeSeconds { get; set; } = Security.TelegramInitDataValidator.DefaultMaxAgeSeconds;

    /// <summary>
    /// HTTPS URL of the Mini App (research studio). When set: MenuButtonWebApp + inline web_app keyboard.
    /// Env: TELEGRAM__WEBAPPURL. Skip menu button when empty.
    /// </summary>
    public string? WebAppUrl { get; set; }
}

public sealed class AssistantClientOptions
{
    public const string SectionName = "Assistant";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://assistant-api:8080";

    [Required]
    [MinLength(16)]
    public string ServiceKey { get; set; } = string.Empty;
}
