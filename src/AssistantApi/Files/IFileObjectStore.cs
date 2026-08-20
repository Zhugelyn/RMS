namespace AssistantApi.Files;

public interface IFileObjectStore
{
    Task InsertAsync(FileObject file, CancellationToken cancellationToken);
    Task<FileObject?> GetAsync(Guid fileId, CancellationToken cancellationToken);
    Task UpdateAsync(FileObject file, CancellationToken cancellationToken);
}
