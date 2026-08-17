using System.Collections.Concurrent;
using AssistantApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AssistantApi.Research;

public sealed class ResearchSettings
{
    public string UserId { get; init; } = string.Empty;
    public string? InstagramHandle { get; init; }
    public bool Enabled { get; init; }
    public int CadenceDays { get; init; } = 14;
    public string? Timezone { get; init; }
    public string? NotifyChatId { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
}

public interface IResearchSettingsStore
{
    Task<ResearchSettings?> GetAsync(string userId, CancellationToken cancellationToken);
    Task UpsertAsync(ResearchSettings settings, CancellationToken cancellationToken);
}

public static class ResearchSettingsDefaults
{
    public const int DefaultCadenceDays = 14;

    public static ResearchSettings Normalize(ResearchSettings s) => new()
    {
        UserId = s.UserId,
        InstagramHandle = s.InstagramHandle,
        Enabled = s.Enabled,
        CadenceDays = s.CadenceDays <= 0 ? DefaultCadenceDays : Math.Clamp(s.CadenceDays, 1, 90),
        Timezone = s.Timezone,
        NotifyChatId = s.NotifyChatId,
        NextRunAt = s.NextRunAt,
        LastRunAt = s.LastRunAt
    };
}

/// <summary>Dev fallback when Postgres is not configured. No Graph/scheduler wiring.</summary>
public sealed class InMemoryResearchSettingsStore : IResearchSettingsStore
{
    private readonly ConcurrentDictionary<string, ResearchSettings> _items = new(StringComparer.Ordinal);

    public Task<ResearchSettings?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        _items.TryGetValue(userId, out var value);
        return Task.FromResult(value);
    }

    public Task UpsertAsync(ResearchSettings settings, CancellationToken cancellationToken)
    {
        _items[settings.UserId] = ResearchSettingsDefaults.Normalize(settings);
        return Task.CompletedTask;
    }
}

public sealed class PostgresResearchSettingsStore : IResearchSettingsStore
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;

    public PostgresResearchSettingsStore(IDbContextFactory<AssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ResearchSettings?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ResearchSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task UpsertAsync(ResearchSettings settings, CancellationToken cancellationToken)
    {
        var normalized = ResearchSettingsDefaults.Normalize(settings);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.ResearchSettings
            .FirstOrDefaultAsync(x => x.UserId == normalized.UserId, cancellationToken);
        if (existing is null)
        {
            db.ResearchSettings.Add(new ResearchSettingsEntity
            {
                UserId = normalized.UserId,
                InstagramHandle = normalized.InstagramHandle,
                Enabled = normalized.Enabled,
                CadenceDays = normalized.CadenceDays,
                Timezone = normalized.Timezone,
                NotifyChatId = normalized.NotifyChatId,
                NextRunAt = normalized.NextRunAt,
                LastRunAt = normalized.LastRunAt,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.InstagramHandle = normalized.InstagramHandle;
            existing.Enabled = normalized.Enabled;
            existing.CadenceDays = normalized.CadenceDays;
            existing.Timezone = normalized.Timezone;
            existing.NotifyChatId = normalized.NotifyChatId;
            existing.NextRunAt = normalized.NextRunAt;
            existing.LastRunAt = normalized.LastRunAt;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ResearchSettings ToModel(ResearchSettingsEntity e) => new()
    {
        UserId = e.UserId,
        InstagramHandle = e.InstagramHandle,
        Enabled = e.Enabled,
        CadenceDays = e.CadenceDays,
        Timezone = e.Timezone,
        NotifyChatId = e.NotifyChatId,
        NextRunAt = e.NextRunAt,
        LastRunAt = e.LastRunAt
    };
}
