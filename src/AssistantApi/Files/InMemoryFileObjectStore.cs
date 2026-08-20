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

    public Task<IReadOnlyList<FileObject>> ListRecentAsync(
        string userId,
        string domain,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var take = Math.Clamp(limit, 1, FileLimits.MaxPackListLimit);
        var list = _items.Values
            .Where(f =>
                string.Equals(f.UserId, userId, StringComparison.Ordinal)
                && string.Equals(f.Domain, domain, StringComparison.OrdinalIgnoreCase)
                && f.Status is FileStatuses.Active or FileStatuses.Uploaded)
            .OrderByDescending(f => f.UploadedAt ?? f.CreatedAt)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<FileObject>>(list);
    }
}
