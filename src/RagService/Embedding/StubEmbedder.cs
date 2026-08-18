using System.Security.Cryptography;
using System.Text;

namespace RagService.Embedding;

/// <summary>
/// Deterministic hash-bag stub (no external embeddings API). Good enough for
/// cosine retrieval in tests and local compose until a real embedder slice.
/// </summary>
public sealed class StubEmbedder : IEmbedder
{
    public const int DefaultDimensions = 64;

    public int Dimensions => DefaultDimensions;

    public float[] Embed(string text)
    {
        var vector = new float[DefaultDimensions];
        if (string.IsNullOrWhiteSpace(text))
        {
            return vector;
        }

        var tokens = text
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '/', '\\', '(', ')', '[', ']', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var idx = BitConverter.ToUInt16(hash, 0) % DefaultDimensions;
            var sign = (hash[2] & 1) == 0 ? 1f : -1f;
            vector[idx] += sign;
        }

        // L2 normalize
        double sumSq = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            sumSq += vector[i] * vector[i];
        }

        if (sumSq <= 0)
        {
            return vector;
        }

        var norm = (float)Math.Sqrt(sumSq);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }
}
