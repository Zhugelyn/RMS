namespace AssistantApi.Research;

/// <summary>
/// Hook after research run: gateway Telegram notify (status/plan text + optional photos).
/// Default without Gateway:BaseUrl = no-op; compose uses GatewayResearchNotifyHook.
/// </summary>
public interface IResearchNotifyHook
{
    Task OnResearchRunAsync(ResearchNotifyEvent evt, CancellationToken cancellationToken);
}

public sealed class ResearchNotifyEvent
{
    public string UserId { get; init; } = string.Empty;
    public string? NotifyChatId { get; init; }
    public string PeriodKey { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    /// <summary>Relative paths under Research:ImageVolumePath (research-media/.../out/..).</summary>
    public IReadOnlyList<string> PhotoPaths { get; init; } = Array.Empty<string>();
}

public sealed class NoOpResearchNotifyHook : IResearchNotifyHook
{
    public Task OnResearchRunAsync(ResearchNotifyEvent evt, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
