using System.Text;
using AssistantApi.Contracts;
using AssistantApi.Memory;
using AssistantApi.Packs;

namespace AssistantApi.Harness;

public interface IPackPromptBuilder
{
    string BuildSpecialistPrompt(AgentPack pack, ChatRequest request, string memoryBlock);

    string BuildRouterPrompt(AgentPack routerPack, ChatRequest request, string? profileHint);

    string BuildMemoryBlock(UserProfile? profile, IReadOnlyList<HarnessEpisode> episodes, int maxChars = 1200);
}

public sealed class PackPromptBuilder : IPackPromptBuilder
{
    public string BuildSpecialistPrompt(AgentPack pack, ChatRequest request, string memoryBlock)
    {
        var sb = new StringBuilder();
        AppendFile(sb, Path.Combine(pack.PromptsDirectoryPath, "system.md"));
        AppendFile(sb, pack.AgentsMarkdownPath, maxChars: 2500);
        if (!string.IsNullOrWhiteSpace(memoryBlock))
        {
            sb.AppendLine("## Harness memory");
            sb.AppendLine(memoryBlock.Trim());
        }

        sb.AppendLine("## Request meta");
        sb.AppendLine($"conversationId={request.ConversationId}");
        sb.AppendLine($"userId={request.UserId}");
        sb.AppendLine($"traceId={request.TraceId}");
        sb.AppendLine($"domainPack={pack.Id}");
        sb.AppendLine("## User");
        sb.Append(request.Text.Trim());
        return sb.ToString();
    }

    public string BuildRouterPrompt(AgentPack routerPack, ChatRequest request, string? profileHint)
    {
        var sb = new StringBuilder();
        AppendFile(sb, Path.Combine(routerPack.PromptsDirectoryPath, "system.md"));
        AppendFile(sb, Path.Combine(routerPack.PromptsDirectoryPath, "classify-hints.md"));
        if (!string.IsNullOrWhiteSpace(profileHint))
        {
            sb.AppendLine("## Profile hint");
            sb.AppendLine(profileHint.Trim());
        }

        sb.AppendLine("## User message");
        sb.AppendLine(request.Text.Trim());
        sb.AppendLine("## Answer format");
        sb.AppendLine("Return exactly one token: salon|marketing|tasks|general");
        return sb.ToString();
    }

    public string BuildMemoryBlock(UserProfile? profile, IReadOnlyList<HarnessEpisode> episodes, int maxChars = 1200)
    {
        var sb = new StringBuilder();
        if (profile is not null)
        {
            if (!string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                sb.AppendLine($"name={profile.DisplayName}");
            }

            if (!string.IsNullOrWhiteSpace(profile.Locale))
            {
                sb.AppendLine($"locale={profile.Locale}");
            }

            if (!string.IsNullOrWhiteSpace(profile.Timezone))
            {
                sb.AppendLine($"tz={profile.Timezone}");
            }

            if (!string.IsNullOrWhiteSpace(profile.Notes))
            {
                sb.AppendLine($"notes={Trim(profile.Notes, 240)}");
            }
        }

        if (episodes.Count > 0)
        {
            sb.AppendLine("episodes:");
            foreach (var ep in episodes)
            {
                sb.AppendLine($"- {Trim(ep.Task, 120)} → {Trim(ep.Result, 160)}");
            }
        }

        var text = sb.ToString().Trim();
        return text.Length <= maxChars ? text : text[..maxChars].TrimEnd() + "…";
    }

    private static void AppendFile(StringBuilder sb, string path, int maxChars = 4000)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var text = File.ReadAllText(path).Trim();
        if (text.Length > maxChars)
        {
            text = text[..maxChars].TrimEnd() + "…";
        }

        sb.AppendLine(text);
        sb.AppendLine();
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";
}
