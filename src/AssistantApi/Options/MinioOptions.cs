using System.ComponentModel.DataAnnotations;

namespace AssistantApi.Options;

/// <summary>MinIO/S3-compatible object store (ADR-015). Empty Endpoint → UnavailablePresigner; soft-fail never breaks /v1/chat.</summary>
public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    /// <summary>Internal endpoint, e.g. http://minio:9000. Empty = files API returns 503 storage unavailable.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Region { get; set; } = "us-east-1";

    public string BucketSalon { get; set; } = "tg-ai-salon";

    public string BucketMarketing { get; set; } = "tg-ai-marketing";

    /// <summary>Presigned URL TTL in seconds (short; clamped 30..3600 at issue time).</summary>
    [Range(30, 3600)]
    public int PresignTtlSeconds { get; set; } = 300;

    /// <summary>Pending upload metadata TTL before considered expired (clamped 60..86400).</summary>
    [Range(60, 86400)]
    public int PendingTtlSeconds { get; set; } = 3600;

    [Range(1024, 104_857_600)]
    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);
}
