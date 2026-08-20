namespace AssistantApi.Files;

public static class FileStatuses
{
    public const string Pending = "pending";
    public const string Uploaded = "uploaded";
    /// <summary>Transient: confirm received, scanner running (stub is sync → rarely observed).</summary>
    public const string Scanning = "scanning";
    public const string Active = "active";
    public const string Rejected = "rejected";
    public const string Deleted = "deleted";
}

public sealed class FileObject
{
    public Guid FileId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public string ObjectKey { get; init; } = string.Empty;
    public string? OriginalFilename { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Status { get; init; } = FileStatuses.Pending;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UploadedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class FileUploadIntentRequest
{
    public int SchemaVersion { get; init; } = 1;
    public string UserId { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? OriginalFilename { get; init; }
}

public sealed class FileUploadIntentResponse
{
    public int SchemaVersion { get; init; } = 1;
    public Guid FileId { get; init; }
    public string Domain { get; init; } = string.Empty;
    public string Status { get; init; } = FileStatuses.Pending;
    public string UploadUrl { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = "PUT";
    public IReadOnlyDictionary<string, string> RequiredHeaders { get; init; } =
        new Dictionary<string, string>();
    public DateTimeOffset UrlExpiresAt { get; init; }
    public DateTimeOffset PendingExpiresAt { get; init; }
}

public sealed class FileConfirmRequest
{
    public int SchemaVersion { get; init; } = 1;
    public string UserId { get; init; } = string.Empty;
}

public sealed class FileDownloadUrlRequest
{
    public int SchemaVersion { get; init; } = 1;
    public string UserId { get; init; } = string.Empty;
}

public sealed class FileDownloadUrlResponse
{
    public int SchemaVersion { get; init; } = 1;
    public Guid FileId { get; init; }
    public string DownloadUrl { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = "GET";
    public DateTimeOffset UrlExpiresAt { get; init; }
}

public sealed class FileMetadataDto
{
    public int SchemaVersion { get; init; } = 1;
    public Guid FileId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? OriginalFilename { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UploadedAt { get; init; }
    // Never expose bucket/objectKey to clients (opaque; server-only).
}

public sealed class FileValidationException : Exception
{
    public FileValidationException(string message) : base(message)
    {
    }
}

public sealed class FileObjectNotFoundException : Exception
{
    public FileObjectNotFoundException(string message) : base(message)
    {
    }
}

public sealed class FileStorageUnavailableException : Exception
{
    public FileStorageUnavailableException(string message = "Object storage is not configured.")
        : base(message)
    {
    }
}

public sealed class FileForbiddenException : Exception
{
    public FileForbiddenException(string message) : base(message)
    {
    }
}
