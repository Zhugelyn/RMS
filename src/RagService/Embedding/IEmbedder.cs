namespace RagService.Embedding;

/// <summary>ADR-014: stub embedder OK until dedicated slice; no new SaaS key.</summary>
public interface IEmbedder
{
    int Dimensions { get; }

    float[] Embed(string text);
}
