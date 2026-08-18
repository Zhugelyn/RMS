using System.Collections.Concurrent;
using RagService.Contracts;
using RagService.Embedding;

namespace RagService.Indexing;

/// <summary>Process-local store for unit tests and runs without Elasticsearch URIs.</summary>
public sealed class InMemoryDocumentIndex : IDocumentIndex
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IndexedDocument>> _byIndex = new();

    public string Mode => "in-memory";

    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpsertAsync(IndexedDocument document, CancellationToken cancellationToken)
    {
        var bucket = _byIndex.GetOrAdd(document.Index, _ => new ConcurrentDictionary<string, IndexedDocument>(StringComparer.Ordinal));
        bucket[document.DocumentId] = document;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(
        RagDomain domain,
        string index,
        float[] queryEmbedding,
        string queryText,
        int topK,
        CancellationToken cancellationToken)
    {
        // Hard isolation: only the requested index is scanned.
        if (!_byIndex.TryGetValue(index, out var bucket) || bucket.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        var hits = bucket.Values
            .Select(doc =>
            {
                var score = StubEmbedder.Cosine(queryEmbedding, doc.Embedding);
                // Tiny lexical boost so short exact phrases still rank when vectors collide.
                if (!string.IsNullOrWhiteSpace(queryText)
                    && doc.Text.Contains(queryText, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.15;
                }

                return new SearchHit
                {
                    DocumentId = doc.DocumentId,
                    Domain = domain.ToString().ToLowerInvariant(),
                    Title = doc.Title,
                    Snippet = Snippet(doc.Text, 240),
                    Score = score
                };
            })
            .Where(h => h.Score > 0)
            .OrderByDescending(h => h.Score)
            .Take(Math.Clamp(topK, 1, 20))
            .ToList();

        return Task.FromResult<IReadOnlyList<SearchHit>>(hits);
    }

    private static string Snippet(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text;
        }

        return text[..max] + "…";
    }
}
