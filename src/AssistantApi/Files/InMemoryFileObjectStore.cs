using System.Collections.Concurrent;

namespace AssistantApi.Files;

public sealed class InMemoryFileObjectStore : IFileObjectStore
{
    private readonly ConcurrentDictionary<Guid, FileObject> _items = new();

    public Task InsertAsync(FileObject file, CancellationToken cancellationToken)
    {
        if (!_items.TryAdd(file.FileId, file))
        {
            throw new InvalidOperationException("File id already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<FileObject?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        _items.TryGetValue(fileId, out var file);
        return Task.FromResult(file);
    }

    public Task UpdateAsync(FileObject file, CancellationToken cancellationToken)
    {
        _items[file.FileId] = file;
        return Task.CompletedTask;
    }
}
