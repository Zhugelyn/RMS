using System.Collections.Concurrent;
using AssistantApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AssistantApi.Research;

public sealed class ResearchSettings
{
    public string UserId { get; init; } = string.Empty;
    public string? InstagramHandle { get; init; }
    /// <summary>Open VK communities allowlist (screen_name / owner_id). Never tokens.</summary>
    public IReadOnlyList<VkCommunityTarget> VkCommunities { get; init; } = Array.Empty<VkCommunityTarget>();
    public bool Enabled { get; init; }
    public int CadenceDays { get; init; } = 14;
    public string? Timezone { get; init; }
    public string? NotifyChatId { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    /// <summary>Last soft failure code/message. Cleared on successful run. Never stores tokens.</summary>
    public string? LastError { get; init; }
}

public interface IResearchSettingsStore
{
    Task<ResearchSettings?> GetAsync(string userId, CancellationToken cancellationToken);
    Task UpsertAsync(ResearchSettings settings, CancellationToken cancellationToken);

    /// <summary>Enabled settings with NextRunAt ≤ now (due for scheduler).</summary>
    Task<IReadOnlyList<ResearchSettings>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public static class ResearchSettingsDefaults
{
    public const int DefaultCadenceDays = 14;

    public static ResearchSettings Normalize(ResearchSettings s) => new()
    {
        UserId = s.UserId,
        InstagramHandle = s.InstagramHandle,
        VkCommunities = VkCommunityAllowlist.Normalize(s.VkCommunities),
        Enabled = s.Enabled,
        CadenceDays = s.CadenceDays <= 0 ? DefaultCadenceDays : Math.Clamp(s.CadenceDays, 1, 90),
        Timezone = s.Timezone,
        NotifyChatId = s.NotifyChatId,
        NextRunAt = s.NextRunAt,
        LastRunAt = s.LastRunAt,
        LastError = string.IsNullOrWhiteSpace(s.LastError) ? null : TrimError(s.LastError)
    };

    private static string TrimError(string value) =>
        value.Length <= 512 ? value : value[..512];
}

/// <summary>Dev fallback when Postgres is not configured.</summary>
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

    public Task<IReadOnlyList<ResearchSettings>> ListDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var due = _items.Values
            .Where(s => ResearchCadence.IsDue(s, now))
            .OrderBy(s => s.NextRunAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<ResearchSettings>>(due);
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
                VkCommunitiesJson = VkCommunityAllowlist.Serialize(normalized.VkCommunities),
                Enabled = normalized.Enabled,
                CadenceDays = normalized.CadenceDays,
                Timezone = normalized.Timezone,
                NotifyChatId = normalized.NotifyChatId,
                NextRunAt = normalized.NextRunAt,
                LastRunAt = normalized.LastRunAt,
                LastError = normalized.LastError,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.InstagramHandle = normalized.InstagramHandle;
            existing.VkCommunitiesJson = VkCommunityAllowlist.Serialize(normalized.VkCommunities);
            existing.Enabled = normalized.Enabled;
            existing.CadenceDays = normalized.CadenceDays;
            existing.Timezone = normalized.Timezone;
            existing.NotifyChatId = normalized.NotifyChatId;
            existing.NextRunAt = normalized.NextRunAt;
            existing.LastRunAt = normalized.LastRunAt;
            existing.LastError = normalized.LastError;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResearchSettings>> ListDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        // Load enabled rows then filter due in-process: SQLite EF cannot translate
        // nullable DateTimeOffset comparisons; Postgres volume is tiny (per-user settings).
        var rows = await db.ResearchSettings.AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync(cancellationToken);
        return rows
            .Where(x => x.NextRunAt is { } due && due <= now)
            .OrderBy(x => x.NextRunAt)
            .Select(ToModel)
            .ToList();
    }

    private static ResearchSettings ToModel(ResearchSettingsEntity e) => new()
    {
        UserId = e.UserId,
        InstagramHandle = e.InstagramHandle,
        VkCommunities = VkCommunityAllowlist.Deserialize(e.VkCommunitiesJson),
        Enabled = e.Enabled,
        CadenceDays = e.CadenceDays,
        Timezone = e.Timezone,
        NotifyChatId = e.NotifyChatId,
        NextRunAt = e.NextRunAt,
        LastRunAt = e.LastRunAt,
        LastError = e.LastError
    };
}
