namespace AssistantApi.Rag;

/// <summary>Used when Rag:BaseUrl is empty — compose/chat stay green without rag-service.</summary>
public sealed class NoOpRagRetriever : IRagRetriever
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<RagHit>> SearchAsync(
        string domain,
        string query,
        int? topK,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RagHit>>(Array.Empty<RagHit>());
    }
}
