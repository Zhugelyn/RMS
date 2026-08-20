using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Files;

public interface IFilePresignService
{
    Task<FileUploadIntentResponse> CreateUploadIntentAsync(
        FileUploadIntentRequest request,
        CancellationToken cancellationToken);

    Task<FileMetadataDto> ConfirmUploadAsync(
        Guid fileId,
        FileConfirmRequest request,
        CancellationToken cancellationToken);

    Task<FileDownloadUrlResponse> CreateDownloadUrlAsync(
        Guid fileId,
        FileDownloadUrlRequest request,
        CancellationToken cancellationToken);

    Task<FileMetadataDto> GetMetadataAsync(
        Guid fileId,
        string userId,
        CancellationToken cancellationToken);
}

public sealed class FilePresignService : IFilePresignService
{
    private readonly IFileObjectStore _store;
    private readonly IObjectStoragePresigner _presigner;
    private readonly IFileContentScanner _scanner;
    private readonly MinioOptions _options;
    private readonly ILogger<FilePresignService> _logger;
    private readonly TimeProvider _clock;

    public FilePresignService(
        IFileObjectStore store,
        IObjectStoragePresigner presigner,
        IFileContentScanner scanner,
        IOptions<MinioOptions> options,
        ILogger<FilePresignService> logger,
        TimeProvider? clock = null)
    {
        _store = store;
        _presigner = presigner;
        _scanner = scanner;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<FileUploadIntentResponse> CreateUploadIntentAsync(
        FileUploadIntentRequest request,
        CancellationToken cancellationToken)
    {
        EnsureStorageAvailable();

        if (request.SchemaVersion != 1)
        {
            throw new FileValidationException("Unsupported schemaVersion. Expected 1.");
        }

        if (!IsTelegramUserId(request.UserId))
        {
            throw new FileValidationException("userId must be tg-<telegramUserId>.");
        }

        var domain = FileDomains.Normalize(request.Domain);

        if (!FileLimits.IsAllowedContentType(request.ContentType))
        {
            throw new FileValidationException("contentType is not in the allowlist.");
        }

        var contentType = FileLimits.NormalizeContentType(request.ContentType);
        var maxBytes = Math.Clamp(_options.MaxUploadBytes, 1024, FileLimits.AbsoluteMaxUploadBytes);
        if (request.SizeBytes <= 0 || request.SizeBytes > maxBytes)
        {
            throw new FileValidationException($"sizeBytes must be 1..{maxBytes}.");
        }

        var filename = SanitizeFilename(request.OriginalFilename);
        var fileId = Guid.NewGuid();
        // Opaque object key: no PII, no sequential id, no user-controlled path.
        var objectKey = fileId.ToString("N");
        var bucket = ResolveBucket(domain);
        var now = _clock.GetUtcNow();
        var pendingTtl = FileLimits.ClampPendingTtlSeconds(_options.PendingTtlSeconds);
        var putTtl = FileLimits.ClampPresignTtlSeconds(_options.PresignTtlSeconds);

        var file = new FileObject
        {
            FileId = fileId,
            UserId = request.UserId,
            Domain = domain,
            Bucket = bucket,
            ObjectKey = objectKey,
            OriginalFilename = filename,
            ContentType = contentType,
            SizeBytes = request.SizeBytes,
            Status = FileStatuses.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(pendingTtl)
        };

        await _store.InsertAsync(file, cancellationToken);

        PresignResult put;
        try
        {
            put = await _presigner.PresignPutAsync(bucket, objectKey, contentType, putTtl, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presign PUT failed for fileId={FileId}", fileId);
            throw new FileStorageUnavailableException("Failed to issue upload URL.");
        }

        return new FileUploadIntentResponse
        {
            SchemaVersion = 1,
            FileId = fileId,
            Domain = domain,
            Status = FileStatuses.Pending,
            UploadUrl = put.Url,
            HttpMethod = "PUT",
            RequiredHeaders = put.RequiredHeaders,
            UrlExpiresAt = put.ExpiresAt,
            PendingExpiresAt = file.ExpiresAt!.Value
        };
    }

    public async Task<FileMetadataDto> ConfirmUploadAsync(
        Guid fileId,
        FileConfirmRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SchemaVersion != 1)
        {
            throw new FileValidationException("Unsupported schemaVersion. Expected 1.");
        }

        if (!IsTelegramUserId(request.UserId))
        {
            throw new FileValidationException("userId must be tg-<telegramUserId>.");
        }

        var existing = await _store.GetAsync(fileId, cancellationToken)
                       ?? throw new FileObjectNotFoundException("File not found.");

        EnsureOwner(existing, request.UserId);

        // Idempotent: already active → return metadata (no re-scan).
        if (existing.Status == FileStatuses.Active)
        {
            return ToDto(existing);
        }

        if (existing.Status is FileStatuses.Deleted or FileStatuses.Rejected)
        {
            throw new FileForbiddenException("File is not confirmable.");
        }

        var now = _clock.GetUtcNow();
        if (existing.Status == FileStatuses.Pending
            && existing.ExpiresAt is { } exp
            && exp < now)
        {
            throw new FileValidationException("Pending upload expired.");
        }

        // pending|uploaded|scanning → run scan hook (no byte proxy through API).
        var uploaded = existing.Status == FileStatuses.Pending
            ? CloneWithStatus(existing, FileStatuses.Uploaded, now, expiresAt: null)
            : existing;
        if (!ReferenceEquals(uploaded, existing))
        {
            await _store.UpdateAsync(uploaded, cancellationToken);
        }

        var scanning = CloneWithStatus(uploaded, FileStatuses.Scanning, now, expiresAt: null);
        await _store.UpdateAsync(scanning, cancellationToken);

        FileScanResult scan;
        try
        {
            scan = await _scanner.ScanAsync(scanning, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Soft-fail: scanner outage must not leave file stuck; do not download bytes.
            _logger.LogWarning(ex, "Scan stub failed fileId={FileId}; treating as clean", scanning.FileId);
            scan = FileScanResult.Clean();
        }

        if (scan.Verdict == FileScanVerdict.Rejected)
        {
            var rejected = CloneWithStatus(scanning, FileStatuses.Rejected, now, expiresAt: null);
            await _store.UpdateAsync(rejected, cancellationToken);
            _logger.LogWarning(
                "Scan rejected fileId={FileId} reason={Reason}",
                rejected.FileId,
                scan.Reason ?? "rejected");
            throw new FileForbiddenException("File failed content scan.");
        }

        var active = CloneWithStatus(scanning, FileStatuses.Active, now, expiresAt: null);
        await _store.UpdateAsync(active, cancellationToken);
        return ToDto(active);
    }

    public async Task<FileDownloadUrlResponse> CreateDownloadUrlAsync(
        Guid fileId,
        FileDownloadUrlRequest request,
        CancellationToken cancellationToken)
    {
        EnsureStorageAvailable();

        if (request.SchemaVersion != 1)
        {
            throw new FileValidationException("Unsupported schemaVersion. Expected 1.");
        }

        if (!IsTelegramUserId(request.UserId))
        {
            throw new FileValidationException("userId must be tg-<telegramUserId>.");
        }

        var existing = await _store.GetAsync(fileId, cancellationToken)
                       ?? throw new FileObjectNotFoundException("File not found.");

        EnsureOwner(existing, request.UserId);

        if (existing.Status is not (FileStatuses.Active or FileStatuses.Uploaded))
        {
            throw new FileForbiddenException("File is not available for download.");
        }

        // Cross-domain: owner already scoped; domain buckets isolate salon ≠ marketing.
        var getTtl = FileLimits.ClampPresignTtlSeconds(_options.PresignTtlSeconds);
        PresignResult get;
        try
        {
            get = await _presigner.PresignGetAsync(
                existing.Bucket,
                existing.ObjectKey,
                getTtl,
                cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presign GET failed for fileId={FileId}", fileId);
            throw new FileStorageUnavailableException("Failed to issue download URL.");
        }

        return new FileDownloadUrlResponse
        {
            SchemaVersion = 1,
            FileId = fileId,
            DownloadUrl = get.Url,
            HttpMethod = "GET",
            UrlExpiresAt = get.ExpiresAt
        };
    }

    public async Task<FileMetadataDto> GetMetadataAsync(
        Guid fileId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!IsTelegramUserId(userId))
        {
            throw new FileValidationException("userId must be tg-<telegramUserId>.");
        }

        var existing = await _store.GetAsync(fileId, cancellationToken)
                       ?? throw new FileObjectNotFoundException("File not found.");

        EnsureOwner(existing, userId);
        return ToDto(existing);
    }

    private void EnsureStorageAvailable()
    {
        if (!_presigner.IsAvailable)
        {
            throw new FileStorageUnavailableException();
        }
    }

    private string ResolveBucket(string domain) => domain switch
    {
        FileDomains.Salon => string.IsNullOrWhiteSpace(_options.BucketSalon)
            ? "tg-ai-salon"
            : _options.BucketSalon.Trim(),
        FileDomains.Marketing => string.IsNullOrWhiteSpace(_options.BucketMarketing)
            ? "tg-ai-marketing"
            : _options.BucketMarketing.Trim(),
        _ => throw new FileValidationException("domain must be salon or marketing.")
    };

    private static void EnsureOwner(FileObject file, string userId)
    {
        if (!string.Equals(file.UserId, userId, StringComparison.Ordinal))
        {
            // Do not leak existence across users.
            throw new FileObjectNotFoundException("File not found.");
        }
    }

    private static FileObject CloneWithStatus(
        FileObject source,
        string status,
        DateTimeOffset uploadedAt,
        DateTimeOffset? expiresAt) =>
        new()
        {
            FileId = source.FileId,
            UserId = source.UserId,
            Domain = source.Domain,
            Bucket = source.Bucket,
            ObjectKey = source.ObjectKey,
            OriginalFilename = source.OriginalFilename,
            ContentType = source.ContentType,
            SizeBytes = source.SizeBytes,
            Status = status,
            CreatedAt = source.CreatedAt,
            UploadedAt = uploadedAt,
            ExpiresAt = expiresAt
        };

    private static FileMetadataDto ToDto(FileObject f) => new()
    {
        SchemaVersion = 1,
        FileId = f.FileId,
        UserId = f.UserId,
        Domain = f.Domain,
        ContentType = f.ContentType,
        SizeBytes = f.SizeBytes,
        Status = f.Status,
        OriginalFilename = f.OriginalFilename,
        CreatedAt = f.CreatedAt,
        UploadedAt = f.UploadedAt
    };

    private static string? SanitizeFilename(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        if (trimmed.Length > FileLimits.MaxOriginalFilenameLength)
        {
            trimmed = trimmed[..FileLimits.MaxOriginalFilenameLength];
        }

        // Display-only; never used as object key. Strip path separators.
        trimmed = trimmed.Replace('/', '_').Replace('\\', '_').Replace("..", "_", StringComparison.Ordinal);
        return trimmed;
    }

    private static bool IsTelegramUserId(string? userId) =>
        !string.IsNullOrWhiteSpace(userId)
        && userId.StartsWith("tg-", StringComparison.Ordinal)
        && userId.Length > 3
        && userId.Length <= 64
        && userId[3..].All(char.IsDigit);
}
