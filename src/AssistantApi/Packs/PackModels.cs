using System.Text.Json.Serialization;

namespace AssistantApi.Packs;

public sealed class AgentPack
{
    public required string Id { get; init; }
    public required string DirectoryPath { get; init; }
    public required PackManifest Manifest { get; init; }
    public required McpManifest Mcp { get; init; }
    public required string AgentsMarkdownPath { get; init; }
    public required string SkillsDirectoryPath { get; init; }
    public required string PromptsDirectoryPath { get; init; }
}

public sealed class PackManifest
{
    public string Id { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool AnswersUser { get; init; }
    public string ResumePolicy { get; init; } = string.Empty;
    public string? Model { get; init; }
    public string? Effort { get; init; }
    public PackRuntimeManifest Runtime { get; init; } = new();
}

public sealed class PackRuntimeManifest
{
    public string Cwd { get; init; } = ".";
    public string SkillsDir { get; init; } = "skills";
    public string PromptsDir { get; init; } = "prompts";
    public string McpConfig { get; init; } = "mcp.json";
}

public sealed class McpManifest
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<string> Allowlist { get; init; } = Array.Empty<string>();

    /// <summary>Reserved MCP server stubs. Must not contain secrets.</summary>
    public IReadOnlyList<McpServerStub> Servers { get; init; } = Array.Empty<McpServerStub>();
}

public sealed class McpServerStub
{
    public string? Name { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object?>? ExtensionData { get; init; }
}

public static class PackResumePolicy
{
    public const string None = "none";
    public const string PerDomainConversation = "per-domain-conversation";

    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        None,
        PerDomainConversation
    };
}

public static class PackIds
{
    public const string Salon = "salon";
    public const string Marketing = "marketing";
    public const string Tasks = "tasks";
    public const string Router = "_router";

    public static readonly string[] Required =
    [
        Salon,
        Marketing,
        Tasks,
        Router
    ];
}
