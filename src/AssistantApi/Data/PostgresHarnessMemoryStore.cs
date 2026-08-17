using AssistantApi.Memory;
using Microsoft.EntityFrameworkCore;

namespace AssistantApi.Data;

/// <summary>Postgres-backed harness memory. Domain episodes never cross-inject.</summary>
public sealed class PostgresHarnessMemoryStore : IHarnessMemoryStore
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;
    private const int EpisodeCap = 50;

    public PostgresHarnessMemoryStore(IDbContextFactory<AssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<UserProfile?> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return entity is null ? null : ToProfile(entity);
    }

    public async Task UpsertProfileAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.UserProfiles.FirstOrDefaultAsync(x => x.UserId == profile.UserId, cancellationToken);
        if (existing is null)
        {
            db.UserProfiles.Add(new UserProfileEntity
            {
                UserId = profile.UserId,
                DisplayName = profile.DisplayName,
                Locale = profile.Locale,
                Timezone = profile.Timezone,
                Notes = profile.Notes,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.DisplayName = profile.DisplayName;
            existing.Locale = profile.Locale;
            existing.Timezone = profile.Timezone;
            existing.Notes = profile.Notes;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HarnessEpisode>> GetRecentEpisodesAsync(
        string userId,
        string domain,
        int limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Max(1, limit);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        // Order client-side: portable across Npgsql and SQLite test provider (DateTimeOffset ORDER BY).
        var rows = await db.HarnessEpisodes.AsNoTracking()
            .Where(x => x.UserId == userId && x.Domain == domain)
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(x => x.At)
            .Take(take)
            .OrderBy(x => x.At)
            .Select(ToEpisode)
            .ToList();
    }

    public async Task AddEpisodeAsync(HarnessEpisode episode, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        db.HarnessEpisodes.Add(new HarnessEpisodeEntity
        {
            UserId = episode.UserId,
            Domain = episode.Domain,
            Task = episode.Task,
            Result = episode.Result,
            At = episode.At,
            ConversationId = episode.ConversationId,
            TraceId = episode.TraceId
        });
        await db.SaveChangesAsync(cancellationToken);

        var existing = await db.HarnessEpisodes
            .Where(x => x.UserId == episode.UserId && x.Domain == episode.Domain)
            .ToListAsync(cancellationToken);
        var overflow = existing
            .OrderByDescending(x => x.At)
            .Skip(EpisodeCap)
            .ToList();
        if (overflow.Count > 0)
        {
            db.HarnessEpisodes.RemoveRange(overflow);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static UserProfile ToProfile(UserProfileEntity e) => new()
    {
        UserId = e.UserId,
        DisplayName = e.DisplayName,
        Locale = e.Locale,
        Timezone = e.Timezone,
        Notes = e.Notes
    };

    private static HarnessEpisode ToEpisode(HarnessEpisodeEntity e) => new()
    {
        UserId = e.UserId,
        Domain = e.Domain,
        Task = e.Task,
        Result = e.Result,
        At = e.At,
        ConversationId = e.ConversationId,
        TraceId = e.TraceId
    };
}
