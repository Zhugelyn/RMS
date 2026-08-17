namespace AssistantApi.Research;

/// <summary>
/// Internal hook for Telegram notify after research (bot /research UI = next slice).
/// Default = no-op stub.
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
}

public sealed class NoOpResearchNotifyHook : IResearchNotifyHook
{
    public Task OnResearchRunAsync(ResearchNotifyEvent evt, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
