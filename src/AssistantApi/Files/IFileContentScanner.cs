namespace AssistantApi.Files;

/// <summary>
/// Content/malware scanning hook for untrusted uploads (ADR-015).
/// Stub implementations must NOT download object bytes through assistant-api.
/// Real antivirus later; PassThrough = always clean after metadata checks.
/// </summary>
public interface IFileContentScanner
{
    Task<FileScanResult> ScanAsync(FileObject file, CancellationToken cancellationToken);
}

public enum FileScanVerdict
{
    Clean = 0,
    Rejected = 1
}

public sealed class FileScanResult
{
    public FileScanVerdict Verdict { get; init; }

    /// <summary>Machine reason for logs/reject; never include secrets or file body.</summary>
    public string? Reason { get; init; }

    public static FileScanResult Clean() => new() { Verdict = FileScanVerdict.Clean };

    public static FileScanResult Reject(string reason) => new()
    {
        Verdict = FileScanVerdict.Rejected,
        Reason = reason
    };
}

/// <summary>
/// Hardening stub: metadata-only pass-through. No MinIO GET, no byte proxy, no external AV.
/// Replace later with real scanner; keep interface stable.
/// </summary>
public sealed class PassThroughFileContentScanner : IFileContentScanner
{
    private readonly ILogger<PassThroughFileContentScanner> _logger;

    public PassThroughFileContentScanner(ILogger<PassThroughFileContentScanner> logger)
    {
        _logger = logger;
    }

    public Task<FileScanResult> ScanAsync(FileObject file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Stub: trust size/MIME already enforced at upload-intent; do not fetch object bytes.
        if (!FileLimits.IsAllowedContentType(file.ContentType))
        {
            _logger.LogWarning(
                "Scan reject fileId={FileId} reason=mime-not-allowed",
                file.FileId);
            return Task.FromResult(FileScanResult.Reject("mime-not-allowed"));
        }

        if (file.SizeBytes <= 0 || file.SizeBytes > FileLimits.AbsoluteMaxUploadBytes)
        {
            _logger.LogWarning(
                "Scan reject fileId={FileId} reason=size-out-of-range",
                file.FileId);
            return Task.FromResult(FileScanResult.Reject("size-out-of-range"));
        }

        _logger.LogInformation(
            "Scan stub clean fileId={FileId} domain={Domain} type={ContentType}",
            file.FileId,
            file.Domain,
            file.ContentType);
        return Task.FromResult(FileScanResult.Clean());
    }
}
