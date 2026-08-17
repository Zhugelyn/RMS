using System.Collections.Concurrent;

namespace AssistantApi.Memory;

/// <summary>
/// Process-local harness memory. Swap to Postgres-backed store when ConnectionStrings:AssistantDb is set (later harden).
/// </summary>
public sealed class InMemoryHarnessMemoryStore : IHarnessMemoryStore
{
    private readonly ConcurrentDictionary<string, UserProfile> _profiles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<HarnessEpisode>> _episodes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task<UserProfile?> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
        _profiles.TryGetValue(userId, out var profile);
        return Task.FromResult(profile);
    }

    public Task UpsertProfileAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        _profiles[profile.UserId] = profile;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HarnessEpisode>> GetRecentEpisodesAsync(
        string userId,
        string domain,
        int limit,
        CancellationToken cancellationToken)
    {
        var key = EpisodeKey(userId, domain);
        if (!_episodes.TryGetValue(key, out var list))
        {
            return Task.FromResult<IReadOnlyList<HarnessEpisode>>(Array.Empty<HarnessEpisode>());
        }

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<HarnessEpisode>>(
                list.OrderByDescending(e => e.At).Take(Math.Max(1, limit)).Reverse().ToList());
        }
    }

    public Task AddEpisodeAsync(HarnessEpisode episode, CancellationToken cancellationToken)
    {
        var key = EpisodeKey(episode.UserId, episode.Domain);
        var list = _episodes.GetOrAdd(key, _ => new List<HarnessEpisode>());
        lock (_gate)
        {
            list.Add(episode);
            const int cap = 50;
            if (list.Count > cap)
            {
                list.RemoveRange(0, list.Count - cap);
            }
        }

        return Task.CompletedTask;
    }

    private static string EpisodeKey(string userId, string domain) =>
        userId.Trim() + "\u001f" + domain.Trim();
}
