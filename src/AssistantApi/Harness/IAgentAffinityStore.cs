namespace AssistantApi.Harness;

public interface IAgentAffinityStore
{
    bool TryGet(string conversationId, string domainPackId, out string agentId);

    void Set(string conversationId, string domainPackId, string agentId);
}
