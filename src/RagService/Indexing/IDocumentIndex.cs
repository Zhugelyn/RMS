using RagService.Contracts;

namespace RagService.Indexing;

public sealed record IndexedDocument(
    string DocumentId,
    RagDomain Domain,
    string Index,
    string? Title,
    string Text,
    float[] Embedding,
    IReadOnlyDictionary<string, string>? Metadata);

public interface IDocumentIndex
{
    string Mode { get; }

    Task EnsureReadyAsync(CancellationToken cancellationToken);

    Task UpsertAsync(IndexedDocument document, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchHit>> SearchAsync(
        RagDomain domain,
        string index,
        float[] queryEmbedding,
        string queryText,
        int topK,
        CancellationToken cancellationToken);
}
