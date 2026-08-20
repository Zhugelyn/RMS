namespace AssistantApi.Files;

public interface IFileObjectStore
{
    Task InsertAsync(FileObject file, CancellationToken cancellationToken);
    Task<FileObject?> GetAsync(Guid fileId, CancellationToken cancellationToken);
    Task UpdateAsync(FileObject file, CancellationToken cancellationToken);

    /// <summary>
    /// Recent files for owner+domain only (active|uploaded). Used by pack inject; never cross-domain.
    /// </summary>
    Task<IReadOnlyList<FileObject>> ListRecentAsync(
        string userId,
        string domain,
        int limit,
        CancellationToken cancellationToken);
}
