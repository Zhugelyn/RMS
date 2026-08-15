namespace AssistantApi.Providers;

public sealed record CursorSdkRunRequest(
    string ApiKey,
    string Prompt,
    string? AgentId,
    string Model);

public sealed record CursorSdkRunResult(
    string AgentId,
    string Text);

public interface ICursorSdkClient
{
    Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken);
}
