using System.Collections.Concurrent;

namespace AssistantApi.Research;

/// <summary>Dev/test fallback when Postgres is not configured.</summary>
public sealed class InMemoryResearchArtifactStore : IResearchArtifactStore
{
    private readonly ConcurrentDictionary<string, List<ResearchSnapshot>> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<ResearchPlan>> _plans = new(StringComparer.Ordinal);
    private long _snapshotSeq;
    private long _planSeq;

    public Task<long> SaveSnapshotAsync(ResearchSnapshot snapshot, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _snapshotSeq);
        var stored = CloneSnapshot(snapshot, id);
        var list = _snapshots.GetOrAdd(snapshot.UserId, _ => new List<ResearchSnapshot>());
        lock (list)
        {
            list.Add(stored);
            list.Sort((a, b) => b.CapturedAt.CompareTo(a.CapturedAt));
            while (list.Count > ResearchArtifactLimits.SnapshotCapPerUser)
            {
                list.RemoveAt(list.Count - 1);
            }
        }

        return Task.FromResult(id);
    }

    public Task<ResearchSnapshot?> GetLatestSnapshotAsync(string userId, CancellationToken cancellationToken)
    {
        if (!_snapshots.TryGetValue(userId, out var list))
        {
            return Task.FromResult<ResearchSnapshot?>(null);
        }

        lock (list)
        {
            return Task.FromResult(list.Count == 0 ? null : list[0]);
        }
    }

    public Task<long> SavePlanAsync(ResearchPlan plan, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _planSeq);
        var stored = ClonePlan(plan, id);
        var list = _plans.GetOrAdd(plan.UserId, _ => new List<ResearchPlan>());
        lock (list)
        {
            list.Add(stored);
            list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            while (list.Count > ResearchArtifactLimits.PlanCapPerUser)
            {
                list.RemoveAt(list.Count - 1);
            }
        }

        return Task.FromResult(id);
    }

    public Task<ResearchPlan?> GetLatestPlanAsync(string userId, CancellationToken cancellationToken)
    {
        if (!_plans.TryGetValue(userId, out var list))
        {
            return Task.FromResult<ResearchPlan?>(null);
        }

        lock (list)
        {
            return Task.FromResult(list.Count == 0 ? null : list[0]);
        }
    }

    private static ResearchSnapshot CloneSnapshot(ResearchSnapshot s, long id) => new()
    {
        Id = id,
        UserId = s.UserId,
        CapturedAt = s.CapturedAt,
        Posts = s.Posts.ToList(),
        Summary = s.Summary,
        PostCount = s.PostCount,
        SourceStatus = s.SourceStatus
    };

    private static ResearchPlan ClonePlan(ResearchPlan p, long id) => new()
    {
        Id = id,
        UserId = p.UserId,
        CreatedAt = p.CreatedAt,
        WindowStart = p.WindowStart,
        WindowEnd = p.WindowEnd,
        Items = p.Items.ToList()
    };
}
