namespace AssistantApi.Research;

/// <summary>
/// Research artifacts store (ADR-010). Structured snapshot/plan in Postgres — not embeddings/ES/RAG.
/// </summary>
public interface IResearchArtifactStore
{
    Task<long> SaveSnapshotAsync(ResearchSnapshot snapshot, CancellationToken cancellationToken);

    Task<ResearchSnapshot?> GetLatestSnapshotAsync(string userId, CancellationToken cancellationToken);

    Task<long> SavePlanAsync(ResearchPlan plan, CancellationToken cancellationToken);

    Task<ResearchPlan?> GetLatestPlanAsync(string userId, CancellationToken cancellationToken);
}
