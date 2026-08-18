namespace RagService.Security;

/// <summary>
/// PII / secret hygiene for RAG logs (phase7-hardening).
/// Never put query text, document body, snippets, metadata values, or service keys in log templates.
/// </summary>
public static class RagPiiSafeLogging
{
    /// <summary>Allowed structured fields: domain, index, documentId, status, hitCount, mode.</summary>
    public static readonly string[] AllowedLogFieldHints =
    [
        "domain",
        "index",
        "documentId",
        "status",
        "hitCount",
        "mode",
        "packId",
        "topK"
    ];

    public static bool LooksLikeForbiddenLogPayload(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        // Heuristic for tests: Log* calls must not interpolate raw Query/Text/Snippet bodies.
        var forbidden = new[]
        {
            "{Query}",
            "{query}",
            "{Text}",
            "{text}",
            "{Snippet}",
            "{snippet}",
            "{ServiceKey}",
            "{Password}",
            "{Detail}" // ES response body may echo content — status-only instead
        };

        return forbidden.Any(f => line.Contains(f, StringComparison.Ordinal));
    }
}
