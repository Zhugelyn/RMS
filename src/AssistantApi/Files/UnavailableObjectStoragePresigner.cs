namespace AssistantApi.Files;

/// <summary>Soft-fail when MinIO is not configured. Does not affect /v1/chat.</summary>
public sealed class UnavailableObjectStoragePresigner : IObjectStoragePresigner
{
    public bool IsAvailable => false;

    public Task<PresignResult> PresignPutAsync(
        string bucket,
        string objectKey,
        string contentType,
        int ttlSeconds,
        CancellationToken cancellationToken) =>
        throw new FileStorageUnavailableException();

    public Task<PresignResult> PresignGetAsync(
        string bucket,
        string objectKey,
        int ttlSeconds,
        CancellationToken cancellationToken) =>
        throw new FileStorageUnavailableException();
}
