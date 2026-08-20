using System.Text;
using AssistantApi.Packs;

namespace AssistantApi.Files;

/// <summary>
/// Builds a short file-metadata inject block for salon|marketing packs only (ADR-015).
/// Router/tasks never receive files. Soft-fail empty/errors → null (chat continues).
/// Never injects bucket/objectKey/presign URLs/secrets.
/// </summary>
public interface IFilesPackInjector
{
    Task<string?> BuildInjectBlockAsync(
        string userId,
        string packId,
        CancellationToken cancellationToken);
}

public sealed class FilesPackInjector : IFilesPackInjector
{
    private readonly IFileObjectStore _store;
    private readonly ILogger<FilesPackInjector> _logger;

    public FilesPackInjector(IFileObjectStore store, ILogger<FilesPackInjector> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<string?> BuildInjectBlockAsync(
        string userId,
        string packId,
        CancellationToken cancellationToken)
    {
        var domain = FileDomains.ForPack(packId);
        if (domain is null)
        {
            return null;
        }

        if (!string.Equals(packId, PackIds.Salon, StringComparison.Ordinal) &&
            !string.Equals(packId, PackIds.Marketing, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        IReadOnlyList<FileObject> files;
        try
        {
            files = await _store.ListRecentAsync(
                userId,
                domain,
                FileLimits.DefaultPackListLimit,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Files inject soft-fail pack={PackId}", packId);
            return null;
        }

        if (files.Count == 0)
        {
            return null;
        }

        // Hard domain isolation: drop any stray cross-domain row.
        var scoped = files
            .Where(f => string.Equals(f.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .Take(FileLimits.DefaultPackListLimit)
            .ToList();
        if (scoped.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## User files ({domain} bucket only, MCP {FileMcp.Files})");
        sb.AppendLine("Metadata only — use assistant-api presign endpoints; never proxy bytes or invent URLs.");
        foreach (var f in scoped)
        {
            var name = string.IsNullOrWhiteSpace(f.OriginalFilename)
                ? "(unnamed)"
                : Trim(f.OriginalFilename!, FileLimits.MaxFilenameInjectChars);
            sb.AppendLine(
                $"- fileId={f.FileId:N} status={f.Status} type={f.ContentType} size={f.SizeBytes} name={name}");
        }

        var text = sb.ToString().Trim();
        if (text.Length <= FileLimits.MaxPackInjectChars)
        {
            return text;
        }

        return text[..FileLimits.MaxPackInjectChars].TrimEnd() + "…";
    }

    private static string Trim(string value, int max)
    {
        var t = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }
}
