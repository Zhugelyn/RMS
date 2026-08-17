namespace AssistantApi.Memory;

public sealed class UserProfile
{
    public string UserId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Locale { get; init; }
    public string? Timezone { get; init; }
    public string? Notes { get; init; }
}

public sealed class HarnessEpisode
{
    public string UserId { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string Task { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public DateTimeOffset At { get; init; }
    public string? ConversationId { get; init; }
    public string? TraceId { get; init; }
}

public interface IHarnessMemoryStore
{
    Task<UserProfile?> GetProfileAsync(string userId, CancellationToken cancellationToken);

    Task UpsertProfileAsync(UserProfile profile, CancellationToken cancellationToken);

    Task<IReadOnlyList<HarnessEpisode>> GetRecentEpisodesAsync(
        string userId,
        string domain,
        int limit,
        CancellationToken cancellationToken);

    Task AddEpisodeAsync(HarnessEpisode episode, CancellationToken cancellationToken);
}
