namespace AssistantApi.Files;

public sealed class PresignResult
{
    public required string Url { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlyDictionary<string, string> RequiredHeaders { get; init; } =
        new Dictionary<string, string>();
}

public interface IObjectStoragePresigner
{
    bool IsAvailable { get; }

    Task<PresignResult> PresignPutAsync(
        string bucket,
        string objectKey,
        string contentType,
        int ttlSeconds,
        CancellationToken cancellationToken);

    Task<PresignResult> PresignGetAsync(
        string bucket,
        string objectKey,
        int ttlSeconds,
        CancellationToken cancellationToken);
}
