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
