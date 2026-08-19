using AssistantApi.Options;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace AssistantApi.Files;

/// <summary>MinIO SDK presigner. Never logs access/secret keys or full URLs with signatures.</summary>
public sealed class MinioObjectStoragePresigner : IObjectStoragePresigner
{
    private readonly IMinioClient _client;
    private readonly ILogger<MinioObjectStoragePresigner> _logger;

    public MinioObjectStoragePresigner(
        IOptions<MinioOptions> options,
        ILogger<MinioObjectStoragePresigner> logger)
    {
        _logger = logger;
        var o = options.Value;
        if (!o.IsConfigured)
        {
            throw new InvalidOperationException("Minio options incomplete.");
        }

        var endpoint = o.Endpoint.Trim();
        var uri = endpoint.Contains("://", StringComparison.Ordinal)
            ? new Uri(endpoint)
            : new Uri("http://" + endpoint);

        var builder = new MinioClient()
            .WithEndpoint(uri.Host, uri.Port > 0 ? uri.Port : (uri.Scheme == Uri.UriSchemeHttps ? 443 : 80))
            .WithCredentials(o.AccessKey, o.SecretKey)
            .WithSSL(string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(o.Region))
        {
            builder = builder.WithRegion(o.Region);
        }

        _client = builder.Build();
    }

    public bool IsAvailable => true;

    public async Task<PresignResult> PresignPutAsync(
        string bucket,
        string objectKey,
        string contentType,
        int ttlSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType
        };

        var args = new PresignedPutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry(ttlSeconds)
            .WithHeaders(headers);

        var url = await _client.PresignedPutObjectAsync(args).ConfigureAwait(false);
        _logger.LogInformation(
            "Issued presigned PUT for bucket={Bucket} objectKeyPrefix={Prefix} ttl={Ttl}",
            bucket,
            objectKey.Length >= 8 ? objectKey[..8] : objectKey,
            ttlSeconds);

        return new PresignResult
        {
            Url = url,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds),
            RequiredHeaders = headers
        };
    }

    public async Task<PresignResult> PresignGetAsync(
        string bucket,
        string objectKey,
        int ttlSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry(ttlSeconds);

        var url = await _client.PresignedGetObjectAsync(args).ConfigureAwait(false);
        _logger.LogInformation(
            "Issued presigned GET for bucket={Bucket} objectKeyPrefix={Prefix} ttl={Ttl}",
            bucket,
            objectKey.Length >= 8 ? objectKey[..8] : objectKey,
            ttlSeconds);

        return new PresignResult
        {
            Url = url,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds)
        };
    }
}
