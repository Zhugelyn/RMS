using AssistantApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AssistantApi.Files;

public sealed class PostgresFileObjectStore : IFileObjectStore
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;

    public PostgresFileObjectStore(IDbContextFactory<AssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task InsertAsync(FileObject file, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.FileObjects.Add(ToEntity(file));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<FileObject?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.FileObjects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FileId == fileId, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task UpdateAsync(FileObject file, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.FileObjects.FirstOrDefaultAsync(x => x.FileId == file.FileId, cancellationToken);
        if (existing is null)
        {
            throw new FileObjectNotFoundException("File not found.");
        }

        existing.Status = file.Status;
        existing.UploadedAt = file.UploadedAt;
        existing.ExpiresAt = file.ExpiresAt;
        existing.OriginalFilename = file.OriginalFilename;
        existing.ContentType = file.ContentType;
        existing.SizeBytes = file.SizeBytes;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static FileObjectEntity ToEntity(FileObject f) => new()
    {
        FileId = f.FileId,
        UserId = f.UserId,
        Domain = f.Domain,
        Bucket = f.Bucket,
        ObjectKey = f.ObjectKey,
        OriginalFilename = f.OriginalFilename,
        ContentType = f.ContentType,
        SizeBytes = f.SizeBytes,
        Status = f.Status,
        CreatedAt = f.CreatedAt,
        UploadedAt = f.UploadedAt,
        ExpiresAt = f.ExpiresAt
    };

    private static FileObject ToModel(FileObjectEntity e) => new()
    {
        FileId = e.FileId,
        UserId = e.UserId,
        Domain = e.Domain,
        Bucket = e.Bucket,
        ObjectKey = e.ObjectKey,
        OriginalFilename = e.OriginalFilename,
        ContentType = e.ContentType,
        SizeBytes = e.SizeBytes,
        Status = e.Status,
        CreatedAt = e.CreatedAt,
        UploadedAt = e.UploadedAt,
        ExpiresAt = e.ExpiresAt
    };
}
