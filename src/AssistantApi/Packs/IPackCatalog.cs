namespace AssistantApi.Packs;

public interface IPackCatalog
{
    string RootPath { get; }

    IReadOnlyCollection<AgentPack> Packs { get; }

    AgentPack GetRequired(string packId);

    bool TryGet(string packId, out AgentPack pack);
}
