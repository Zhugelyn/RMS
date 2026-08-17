using AssistantApi.Research;
using Microsoft.EntityFrameworkCore;

namespace AssistantApi.Data;

/// <summary>Postgres-backed research snapshots/plans. Cap last K per user. No embeddings.</summary>
public sealed class PostgresResearchArtifactStore : IResearchArtifactStore
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;

    public PostgresResearchArtifactStore(IDbContextFactory<AssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<long> SaveSnapshotAsync(ResearchSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var entity = new ResearchSnapshotEntity
        {
            UserId = snapshot.UserId,
            CapturedAt = snapshot.CapturedAt,
            PayloadJson = ResearchArtifactJson.SerializeSnapshot(snapshot)
        };
        db.ResearchSnapshots.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var existing = await db.ResearchSnapshots
            .Where(x => x.UserId == snapshot.UserId)
            .ToListAsync(cancellationToken);
        var overflow = existing
            .OrderByDescending(x => x.CapturedAt)
            .Skip(ResearchArtifactLimits.SnapshotCapPerUser)
            .ToList();
        if (overflow.Count > 0)
        {
            db.ResearchSnapshots.RemoveRange(overflow);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<ResearchSnapshot?> GetLatestSnapshotAsync(string userId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ResearchSnapshots.AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        var entity = rows.OrderByDescending(x => x.CapturedAt).FirstOrDefault();
        return entity is null ? null : ToSnapshot(entity);
    }

    public async Task<long> SavePlanAsync(ResearchPlan plan, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var entity = new ResearchPlanEntity
        {
            UserId = plan.UserId,
            CreatedAt = plan.CreatedAt,
            WindowStart = plan.WindowStart,
            WindowEnd = plan.WindowEnd,
            PayloadJson = ResearchArtifactJson.SerializePlan(plan)
        };
        db.ResearchPlans.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var existing = await db.ResearchPlans
            .Where(x => x.UserId == plan.UserId)
            .ToListAsync(cancellationToken);
        var overflow = existing
            .OrderByDescending(x => x.CreatedAt)
            .Skip(ResearchArtifactLimits.PlanCapPerUser)
            .ToList();
        if (overflow.Count > 0)
        {
            db.ResearchPlans.RemoveRange(overflow);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<ResearchPlan?> GetLatestPlanAsync(string userId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ResearchPlans.AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        var entity = rows.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return entity is null ? null : ToPlan(entity);
    }

    private static ResearchSnapshot ToSnapshot(ResearchSnapshotEntity e)
    {
        var payload = ResearchArtifactJson.DeserializeSnapshot(e.PayloadJson);
        return new ResearchSnapshot
        {
            Id = e.Id,
            UserId = e.UserId,
            CapturedAt = e.CapturedAt,
            Posts = payload.Posts,
            Summary = payload.Summary,
            PostCount = payload.PostCount > 0 ? payload.PostCount : payload.Posts.Count,
            SourceStatus = payload.SourceStatus
        };
    }

    private static ResearchPlan ToPlan(ResearchPlanEntity e)
    {
        var payload = ResearchArtifactJson.DeserializePlan(e.PayloadJson);
        return new ResearchPlan
        {
            Id = e.Id,
            UserId = e.UserId,
            CreatedAt = e.CreatedAt,
            WindowStart = e.WindowStart,
            WindowEnd = e.WindowEnd,
            Items = payload.Items
        };
    }
}
