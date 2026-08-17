using System.Collections.Concurrent;
using AssistantApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AssistantApi.Research;

/// <summary>Idempotent successful research runs keyed by userId + period (UTC date of due window).</summary>
public interface IResearchScheduleRunStore
{
    Task<bool> HasSuccessfulRunAsync(string userId, string periodKey, CancellationToken cancellationToken);

    Task MarkSuccessfulAsync(
        string userId,
        string periodKey,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}

public sealed class InMemoryResearchScheduleRunStore : IResearchScheduleRunStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _done = new(StringComparer.Ordinal);

    private static string Key(string userId, string periodKey) => userId + "\u001f" + periodKey;

    public Task<bool> HasSuccessfulRunAsync(string userId, string periodKey, CancellationToken cancellationToken) =>
        Task.FromResult(_done.ContainsKey(Key(userId, periodKey)));

    public Task MarkSuccessfulAsync(
        string userId,
        string periodKey,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        _done.TryAdd(Key(userId, periodKey), completedAt);
        return Task.CompletedTask;
    }
}

public sealed class PostgresResearchScheduleRunStore : IResearchScheduleRunStore
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;

    public PostgresResearchScheduleRunStore(IDbContextFactory<AssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<bool> HasSuccessfulRunAsync(
        string userId,
        string periodKey,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ResearchScheduleRuns.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.PeriodKey == periodKey, cancellationToken);
    }

    public async Task MarkSuccessfulAsync(
        string userId,
        string periodKey,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var exists = await db.ResearchScheduleRuns
            .AnyAsync(x => x.UserId == userId && x.PeriodKey == periodKey, cancellationToken);
        if (exists)
        {
            return;
        }

        db.ResearchScheduleRuns.Add(new ResearchScheduleRunEntity
        {
            UserId = userId,
            PeriodKey = periodKey,
            CompletedAt = completedAt
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent tick raced on unique (userId, period) — treat as already recorded.
        }
    }
}
