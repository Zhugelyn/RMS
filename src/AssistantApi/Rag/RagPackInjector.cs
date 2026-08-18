using System.Text;
using AssistantApi.Options;
using AssistantApi.Packs;
using Microsoft.Extensions.Options;

namespace AssistantApi.Rag;

/// <summary>
/// Builds a short KB inject block for salon|marketing packs only (ADR-014).
/// Router/tasks never receive RAG. Soft-fail empty/errors → null (chat continues).
/// </summary>
public interface IRagPackInjector
{
    Task<string?> BuildInjectBlockAsync(string packId, string query, CancellationToken cancellationToken);
}

public sealed class RagPackInjector : IRagPackInjector
{
    private readonly IRagRetriever _retriever;
    private readonly RagOptions _options;
    private readonly ILogger<RagPackInjector> _logger;

    public RagPackInjector(
        IRagRetriever retriever,
        IOptions<RagOptions> options,
        ILogger<RagPackInjector> logger)
    {
        _retriever = retriever;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> BuildInjectBlockAsync(
        string packId,
        string query,
        CancellationToken cancellationToken)
    {
        var domain = RagDomains.ForPack(packId);
        if (domain is null)
        {
            return null;
        }

        // Guard: mcp allowlist is pack-declared; injector only fires for RAG packs.
        if (!string.Equals(packId, PackIds.Salon, StringComparison.Ordinal) &&
            !string.Equals(packId, PackIds.Marketing, StringComparison.Ordinal))
        {
            return null;
        }

        IReadOnlyList<RagHit> hits;
        try
        {
            hits = await _retriever.SearchAsync(domain, query, _options.TopK, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RAG inject soft-fail pack={PackId}", packId);
            return null;
        }

        if (hits.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## Knowledge base hits ({domain} only, MCP {RagMcp.KbRetriever})");
        foreach (var hit in hits.Take(_options.TopK))
        {
            var title = string.IsNullOrWhiteSpace(hit.Title) ? hit.DocumentId : hit.Title.Trim();
            sb.AppendLine(
                $"- [{Trim(title, 80)}] score={hit.Score:0.###}: {Trim(hit.Snippet, 280)}");
        }

        var text = sb.ToString().Trim();
        if (text.Length <= _options.InjectMaxChars)
        {
            return text;
        }

        return text[.._options.InjectMaxChars].TrimEnd() + "…";
    }

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var t = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }
}
