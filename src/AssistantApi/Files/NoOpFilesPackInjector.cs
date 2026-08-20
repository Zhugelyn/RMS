namespace AssistantApi.Files;

/// <summary>No-op files inject (tests / packs without file context).</summary>
public sealed class NoOpFilesPackInjector : IFilesPackInjector
{
    public Task<string?> BuildInjectBlockAsync(
        string userId,
        string packId,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
