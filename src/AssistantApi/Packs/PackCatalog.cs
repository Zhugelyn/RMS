using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AssistantApi.Packs;

public sealed class PackCatalog : IPackCatalog
{
    private static readonly Regex PackIdPattern = new("^[a-z_][a-z0-9_-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly string[] ForbiddenMcpPropertyNames =
    [
        "apikey", "api_key", "api-key", "token", "secret", "password", "authorization", "bearer", "cursorapikey"
    ];

    private readonly Dictionary<string, AgentPack> _packs;

    public PackCatalog(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Agent packs root path is required.", nameof(rootPath));
        }

        RootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(RootPath))
        {
            throw new DirectoryNotFoundException($"Agent packs root not found: {RootPath}");
        }

        _packs = LoadAll(RootPath);
        ValidateCatalog(_packs);
    }

    public string RootPath { get; }

    public IReadOnlyCollection<AgentPack> Packs => new ReadOnlyCollection<AgentPack>(_packs.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList());

    public AgentPack GetRequired(string packId)
    {
        if (TryGet(packId, out var pack))
        {
            return pack;
        }

        throw new KeyNotFoundException($"Unknown agent pack '{packId}'.");
    }

    public bool TryGet(string packId, out AgentPack pack) =>
        _packs.TryGetValue(packId, out pack!);

    public static string ResolveRootPath(string? configuredRoot, string contentRootPath, string? baseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.IsPathRooted(configuredRoot)
                ? Path.GetFullPath(configuredRoot)
                : Path.GetFullPath(Path.Combine(contentRootPath, configuredRoot));
        }

        baseDirectory ??= AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "AgentPacks"),
            Path.Combine(contentRootPath, "AgentPacks"),
            Path.Combine(contentRootPath, "..", "AgentPacks"),
            Path.Combine(contentRootPath, "..", "..", "AgentPacks"),
            Path.Combine(contentRootPath, "..", "..", "src", "AgentPacks")
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full) && File.Exists(Path.Combine(full, "salon", "pack.json")))
            {
                return full;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not resolve AgentPacks root. Set AgentPacks:RootPath or ship packs next to the app.");
    }

    public static PackCatalog LoadFromOptions(IOptions<AgentPacksOptions> options, string contentRootPath) =>
        new(ResolveRootPath(options.Value.RootPath, contentRootPath));

    private static Dictionary<string, AgentPack> LoadAll(string rootPath)
    {
        var result = new Dictionary<string, AgentPack>(StringComparer.Ordinal);
        foreach (var dir in Directory.GetDirectories(rootPath))
        {
            var packId = Path.GetFileName(dir);
            if (packId.StartsWith('.'))
            {
                continue;
            }

            // Skip schema/docs-only files at root; only directories with pack.json are packs.
            if (!File.Exists(Path.Combine(dir, "pack.json")))
            {
                continue;
            }

            var pack = LoadPack(dir, packId);
            if (result.ContainsKey(pack.Id))
            {
                throw new InvalidOperationException($"Duplicate pack id '{pack.Id}'.");
            }

            result[pack.Id] = pack;
        }

        return result;
    }

    private static AgentPack LoadPack(string directoryPath, string folderName)
    {
        var errors = new List<string>();
        var packJsonPath = Path.Combine(directoryPath, "pack.json");
        var agentsPath = Path.Combine(directoryPath, "AGENTS.md");
        var skillsDir = Path.Combine(directoryPath, "skills");
        var promptsDir = Path.Combine(directoryPath, "prompts");
        var mcpPath = Path.Combine(directoryPath, "mcp.json");

        RequireFile(agentsPath, "AGENTS.md", errors);
        RequireDirectory(skillsDir, "skills/", errors);
        RequireDirectory(promptsDir, "prompts/", errors);
        RequireFile(mcpPath, "mcp.json", errors);
        RequireFile(packJsonPath, "pack.json", errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Pack '{folderName}' layout invalid: {string.Join("; ", errors)}");
        }

        PackManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(packJsonPath), JsonOptions)
                       ?? throw new InvalidOperationException("pack.json deserialized to null.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Pack '{folderName}' pack.json parse failed: {ex.Message}", ex);
        }

        ValidateManifest(folderName, directoryPath, manifest);

        McpManifest mcp;
        try
        {
            var mcpRaw = File.ReadAllText(mcpPath);
            RejectSecretLikeJsonKeys(mcpRaw, folderName);
            mcp = JsonSerializer.Deserialize<McpManifest>(mcpRaw, JsonOptions)
                  ?? throw new InvalidOperationException("mcp.json deserialized to null.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Pack '{folderName}' mcp.json parse failed: {ex.Message}", ex);
        }

        ValidateMcp(folderName, mcp);

        // Ensure runtime-relative paths exist inside pack dir.
        var resolvedSkills = Path.GetFullPath(Path.Combine(directoryPath, manifest.Runtime.SkillsDir));
        var resolvedPrompts = Path.GetFullPath(Path.Combine(directoryPath, manifest.Runtime.PromptsDir));
        var resolvedMcp = Path.GetFullPath(Path.Combine(directoryPath, manifest.Runtime.McpConfig));
        EnsureUnderPackRoot(directoryPath, resolvedSkills, "runtime.skillsDir", folderName);
        EnsureUnderPackRoot(directoryPath, resolvedPrompts, "runtime.promptsDir", folderName);
        EnsureUnderPackRoot(directoryPath, resolvedMcp, "runtime.mcpConfig", folderName);
        if (!Directory.Exists(resolvedSkills))
        {
            throw new InvalidOperationException($"Pack '{folderName}' runtime.skillsDir missing: {manifest.Runtime.SkillsDir}");
        }

        if (!Directory.Exists(resolvedPrompts))
        {
            throw new InvalidOperationException($"Pack '{folderName}' runtime.promptsDir missing: {manifest.Runtime.PromptsDir}");
        }

        if (!File.Exists(resolvedMcp))
        {
            throw new InvalidOperationException($"Pack '{folderName}' runtime.mcpConfig missing: {manifest.Runtime.McpConfig}");
        }

        RequirePromptFiles(folderName, resolvedPrompts);

        return new AgentPack
        {
            Id = manifest.Id,
            DirectoryPath = Path.GetFullPath(directoryPath),
            Manifest = manifest,
            Mcp = mcp,
            AgentsMarkdownPath = Path.GetFullPath(agentsPath),
            SkillsDirectoryPath = resolvedSkills,
            PromptsDirectoryPath = resolvedPrompts
        };
    }

    private static void ValidateManifest(string folderName, string directoryPath, PackManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Pack '{folderName}' schemaVersion must be 1.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Id) || !PackIdPattern.IsMatch(manifest.Id))
        {
            throw new InvalidOperationException($"Pack '{folderName}' has invalid id '{manifest.Id}'.");
        }

        if (!string.Equals(manifest.Id, folderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Pack folder '{folderName}' must match pack.json id '{manifest.Id}'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            throw new InvalidOperationException($"Pack '{folderName}' displayName is required.");
        }

        if (!PackResumePolicy.Allowed.Contains(manifest.ResumePolicy))
        {
            throw new InvalidOperationException(
                $"Pack '{folderName}' resumePolicy '{manifest.ResumePolicy}' is invalid.");
        }

        if (manifest.Effort is not null &&
            manifest.Effort is not ("low" or "medium" or "high"))
        {
            throw new InvalidOperationException($"Pack '{folderName}' effort '{manifest.Effort}' is invalid.");
        }

        if (manifest.Runtime is null)
        {
            throw new InvalidOperationException($"Pack '{folderName}' runtime is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Runtime.Cwd) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.SkillsDir) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.PromptsDir) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.McpConfig))
        {
            throw new InvalidOperationException($"Pack '{folderName}' runtime paths must be non-empty.");
        }

        if (string.Equals(manifest.Id, PackIds.Router, StringComparison.Ordinal))
        {
            if (manifest.AnswersUser)
            {
                throw new InvalidOperationException("Pack '_router' must have answersUser=false.");
            }

            if (!string.Equals(manifest.ResumePolicy, PackResumePolicy.None, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Pack '_router' must have resumePolicy=none.");
            }
        }
        else if (!manifest.AnswersUser)
        {
            throw new InvalidOperationException($"Specialist pack '{folderName}' must have answersUser=true.");
        }

        _ = directoryPath;
    }

    private static void ValidateMcp(string folderName, McpManifest mcp)
    {
        if (mcp.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Pack '{folderName}' mcp.json schemaVersion must be 1.");
        }

        var allowlist = mcp.Allowlist ?? Array.Empty<string>();
        var servers = mcp.Servers ?? Array.Empty<McpServerStub>();

        // Layout slice: allowlist stays empty until MCP wiring slice.
        if (allowlist.Count > 0)
        {
            throw new InvalidOperationException(
                $"Pack '{folderName}' mcp allowlist must be empty until MCP wiring slice.");
        }

        foreach (var server in servers)
        {
            if (server.ExtensionData is null)
            {
                continue;
            }

            foreach (var key in server.ExtensionData.Keys)
            {
                if (IsForbiddenSecretKey(key))
                {
                    throw new InvalidOperationException(
                        $"Pack '{folderName}' mcp.json must not contain secret field '{key}'.");
                }
            }
        }
    }

    private static void ValidateCatalog(Dictionary<string, AgentPack> packs)
    {
        foreach (var required in PackIds.Required)
        {
            if (!packs.ContainsKey(required))
            {
                throw new InvalidOperationException($"Required agent pack '{required}' is missing.");
            }
        }

        var router = packs[PackIds.Router];
        if (router.Manifest.AnswersUser ||
            !string.Equals(router.Manifest.ResumePolicy, PackResumePolicy.None, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Router pack invariants violated (answersUser/resumePolicy).");
        }
    }

    private static void RequirePromptFiles(string folderName, string promptsDir)
    {
        foreach (var name in new[] { "system.md", "classify-hints.md", "verify.md" })
        {
            var path = Path.Combine(promptsDir, name);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Pack '{folderName}' missing prompts/{name}.");
            }
        }
    }

    private static void RequireFile(string path, string label, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"missing {label}");
        }
    }

    private static void RequireDirectory(string path, string label, List<string> errors)
    {
        if (!Directory.Exists(path))
        {
            errors.Add($"missing {label}");
        }
    }

    private static void EnsureUnderPackRoot(string packRoot, string resolved, string field, string packId)
    {
        var root = Path.GetFullPath(packRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(resolved);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Pack '{packId}' {field} escapes pack root.");
        }
    }

    private static void RejectSecretLikeJsonKeys(string json, string packId)
    {
        using var doc = JsonDocument.Parse(json);
        RejectSecretLikeElement(doc.RootElement, packId);
    }

    private static void RejectSecretLikeElement(JsonElement element, string packId)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (IsForbiddenSecretKey(prop.Name))
                {
                    throw new InvalidOperationException(
                        $"Pack '{packId}' mcp.json must not contain secret field '{prop.Name}'.");
                }

                RejectSecretLikeElement(prop.Value, packId);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectSecretLikeElement(item, packId);
            }
        }
    }

    private static bool IsForbiddenSecretKey(string name)
    {
        var normalized = name.Trim().ToLowerInvariant().Replace("-", "", StringComparison.Ordinal);
        return ForbiddenMcpPropertyNames.Any(f =>
            normalized.Equals(f.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal), StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.EndsWith("token", StringComparison.Ordinal));
    }
}
