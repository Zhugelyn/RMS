namespace AssistantApi.Rag;

/// <summary>Test/DI helper when RAG inject must be a no-op without constructing options.</summary>
public sealed class NoOpRagPackInjector : IRagPackInjector
{
    public Task<string?> BuildInjectBlockAsync(string packId, string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
