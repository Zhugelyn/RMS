using System.Text;
using AssistantApi.Packs;

namespace AssistantApi.Research;

/// <summary>
/// Builds a short inject block for marketing pack only: latest snapshot summary + last plan.
/// Salon/tasks must never receive this (domain isolation).
/// </summary>
public interface IResearchPackInjector
{
    /// <summary>Returns inject text for marketing pack, or null/empty for other packs / missing artifacts.</summary>
    Task<string?> BuildInjectBlockAsync(string userId, string packId, CancellationToken cancellationToken);
}

public sealed class ResearchPackInjector : IResearchPackInjector
{
    private readonly IResearchArtifactStore _artifacts;

    public ResearchPackInjector(IResearchArtifactStore artifacts)
    {
        _artifacts = artifacts;
    }

    public async Task<string?> BuildInjectBlockAsync(string userId, string packId, CancellationToken cancellationToken)
    {
        if (!string.Equals(packId, PackIds.Marketing, StringComparison.Ordinal))
        {
            return null;
        }

        ResearchSnapshot? snapshot;
        ResearchPlan? plan;
        try
        {
            snapshot = await _artifacts.GetLatestSnapshotAsync(userId, cancellationToken);
            plan = await _artifacts.GetLatestPlanAsync(userId, cancellationToken);
        }
        catch
        {
            // Soft: inject failure must not break chat.
            return null;
        }

        if (snapshot is null && plan is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Research artifacts (marketing only, not RAG)");
        if (snapshot is not null)
        {
            sb.AppendLine($"snapshot@{snapshot.CapturedAt:u} posts={snapshot.PostCount}: {Trim(snapshot.Summary, 360)}");
        }

        if (plan is not null)
        {
            sb.AppendLine(
                $"plan window={plan.WindowStart:yyyy-MM-dd}..{plan.WindowEnd:yyyy-MM-dd} items={plan.Items.Count}");
            foreach (var item in plan.Items.Take(5))
            {
                sb.AppendLine(
                    $"- {item.Date:yyyy-MM-dd} [{item.Status}] {Trim(item.Caption, 100)} | prompt={Trim(item.ImagePrompt, 80)}");
            }

            if (plan.Items.Count > 5)
            {
                sb.AppendLine($"- … +{plan.Items.Count - 5} more days");
            }
        }

        var text = sb.ToString().Trim();
        return text.Length <= ResearchArtifactLimits.InjectMaxChars
            ? text
            : text[..ResearchArtifactLimits.InjectMaxChars].TrimEnd() + "…";
    }

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var t = value.Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }
}
