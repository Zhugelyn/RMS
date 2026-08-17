namespace AssistantApi.Providers;

public sealed record CursorSdkRunRequest(
    string ApiKey,
    string Prompt,
    string? AgentId,
    string Model,
    string? PackId = null);

public sealed record CursorSdkRunResult(
    string AgentId,
    string Text,
    string? PackId = null);

public interface ICursorSdkClient
{
    Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken);
}
