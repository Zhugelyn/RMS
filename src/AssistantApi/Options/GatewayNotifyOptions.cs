namespace AssistantApi.Options;

/// <summary>
/// Optional reverse call assistant-api → telegram-gateway for research notify.
/// Empty BaseUrl → NoOp hook (tests / local without gateway).
/// </summary>
public sealed class GatewayNotifyOptions
{
    public const string SectionName = "Gateway";

    /// <summary>e.g. http://telegram-gateway:8080</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Same inter-service key as Assistant:ServiceKey.</summary>
    public string ServiceKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;
}
