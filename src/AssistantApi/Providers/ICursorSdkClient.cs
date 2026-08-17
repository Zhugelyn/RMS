namespace AssistantApi.Providers;

public sealed record CursorSdkRunRequest(
    string ApiKey,
    string Prompt,
    string? AgentId,
    string Model,
    string? PackId = null,
    string? LocalCwd = null,
    bool CollectImages = false,
    int? ImageCap = null);

public sealed class CursorSdkRunResult
{
    public CursorSdkRunResult(
        string agentId,
        string text,
        string? packId = null,
        IReadOnlyList<string>? images = null,
        string? error = null)
    {
        AgentId = agentId;
        Text = text;
        PackId = packId;
        Images = images ?? Array.Empty<string>();
        Error = error;
    }

    public string AgentId { get; }
    public string Text { get; }
    public string? PackId { get; }
    public IReadOnlyList<string> Images { get; }
    public string? Error { get; }
}

public interface ICursorSdkClient
{
    Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken);
}
