using System.Collections.Concurrent;

namespace AssistantApi.Harness;

public sealed class InMemoryAgentAffinityStore : IAgentAffinityStore
{
    private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.Ordinal);

    public bool TryGet(string conversationId, string domainPackId, out string agentId) =>
        _map.TryGetValue(Key(conversationId, domainPackId), out agentId!);

    public void Set(string conversationId, string domainPackId, string agentId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) ||
            string.IsNullOrWhiteSpace(domainPackId) ||
            string.IsNullOrWhiteSpace(agentId))
        {
            return;
        }

        _map[Key(conversationId, domainPackId)] = agentId;
    }

    private static string Key(string conversationId, string domainPackId) =>
        conversationId.Trim() + "\u001f" + domainPackId.Trim();
}
