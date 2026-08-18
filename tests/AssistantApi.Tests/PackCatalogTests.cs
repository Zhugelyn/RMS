using AssistantApi.Packs;

namespace AssistantApi.Tests;

public sealed class PackCatalogTests
{
    private static string FindPacksRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AgentPacks");
            if (Directory.Exists(Path.Combine(candidate, "salon")))
            {
                return candidate;
            }

            // Output copy from csproj Content Link
            var outputCopy = Path.Combine(dir.FullName, "AgentPacks");
            if (Directory.Exists(Path.Combine(outputCopy, "salon")))
            {
                return outputCopy;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("src/AgentPacks not found from test base directory.");
    }

    [Fact]
    public void Loads_required_packs_with_layout_invariants()
    {
        var catalog = new PackCatalog(FindPacksRoot());

        Assert.Equal(PackIds.Required.OrderBy(x => x), catalog.Packs.Select(p => p.Id).OrderBy(x => x));

        var router = catalog.GetRequired(PackIds.Router);
        Assert.False(router.Manifest.AnswersUser);
        Assert.Equal(PackResumePolicy.None, router.Manifest.ResumePolicy);

        foreach (var id in new[] { PackIds.Salon, PackIds.Marketing, PackIds.Tasks })
        {
            var pack = catalog.GetRequired(id);
            Assert.True(pack.Manifest.AnswersUser);
            Assert.Equal(PackResumePolicy.PerDomainConversation, pack.Manifest.ResumePolicy);
            Assert.True(File.Exists(pack.AgentsMarkdownPath));
            Assert.True(Directory.Exists(pack.SkillsDirectoryPath));
            Assert.True(Directory.Exists(pack.PromptsDirectoryPath));
            Assert.Equal(1, pack.Manifest.SchemaVersion);
            Assert.False(string.IsNullOrWhiteSpace(pack.Manifest.Model));
        }

        // Phase 7: salon/marketing allowlist kb-retriever; router/tasks empty.
        Assert.Equal(new[] { "kb-retriever" }, catalog.GetRequired(PackIds.Salon).Mcp.Allowlist);
        Assert.Equal(new[] { "kb-retriever" }, catalog.GetRequired(PackIds.Marketing).Mcp.Allowlist);
        Assert.Empty(catalog.GetRequired(PackIds.Tasks).Mcp.Allowlist);
        Assert.Empty(catalog.GetRequired(PackIds.Router).Mcp.Allowlist);
        Assert.True(File.Exists(Path.Combine(catalog.GetRequired(PackIds.Salon).SkillsDirectoryPath, "kb-retrieve", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(catalog.GetRequired(PackIds.Marketing).SkillsDirectoryPath, "kb-retrieve", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(catalog.GetRequired(PackIds.Tasks).SkillsDirectoryPath, "kb-retrieve", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(catalog.GetRequired(PackIds.Router).SkillsDirectoryPath, "kb-retrieve", "SKILL.md")));
    }

    [Fact]
    public void Rejects_router_when_answers_user()
    {
        using var tmp = TempPackRoot.CreateFrom(FindPacksRoot());
        var packJson = Path.Combine(tmp.Root, "_router", "pack.json");
        var text = File.ReadAllText(packJson).Replace("\"answersUser\": false", "\"answersUser\": true", StringComparison.Ordinal);
        File.WriteAllText(packJson, text);

        var ex = Assert.Throws<InvalidOperationException>(() => new PackCatalog(tmp.Root));
        Assert.Contains("answersUser=false", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_mcp_secret_fields()
    {
        using var tmp = TempPackRoot.CreateFrom(FindPacksRoot());
        File.WriteAllText(Path.Combine(tmp.Root, "salon", "mcp.json"), """
            {
              "schemaVersion": 1,
              "allowlist": ["kb-retriever"],
              "servers": [ { "name": "kb-retriever", "apiKey": "sk-leak" } ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => new PackCatalog(tmp.Root));
        Assert.Contains("secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_foreign_allowlist_on_marketing()
    {
        using var tmp = TempPackRoot.CreateFrom(FindPacksRoot());
        File.WriteAllText(Path.Combine(tmp.Root, "marketing", "mcp.json"), """
            {
              "schemaVersion": 1,
              "allowlist": ["future-direct"],
              "servers": []
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => new PackCatalog(tmp.Root));
        Assert.Contains("allowlist", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_rag_allowlist_on_tasks()
    {
        using var tmp = TempPackRoot.CreateFrom(FindPacksRoot());
        File.WriteAllText(Path.Combine(tmp.Root, "tasks", "mcp.json"), """
            {
              "schemaVersion": 1,
              "allowlist": ["kb-retriever"],
              "servers": [ { "name": "kb-retriever" } ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => new PackCatalog(tmp.Root));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_salon_without_kb_retriever()
    {
        using var tmp = TempPackRoot.CreateFrom(FindPacksRoot());
        File.WriteAllText(Path.Combine(tmp.Root, "salon", "mcp.json"), """
            {
              "schemaVersion": 1,
              "allowlist": [],
              "servers": []
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => new PackCatalog(tmp.Root));
        Assert.Contains("kb-retriever", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRootPath_finds_repo_packs()
    {
        var root = PackCatalog.ResolveRootPath(
            configuredRoot: null,
            contentRootPath: Path.Combine(FindPacksRoot(), "..", "AssistantApi"));
        Assert.True(Directory.Exists(Path.Combine(root, "salon")));
    }

    private sealed class TempPackRoot : IDisposable
    {
        public string Root { get; }

        private TempPackRoot(string root) => Root = root;

        public static TempPackRoot CreateFrom(string sourceRoot)
        {
            var root = Path.Combine(Path.GetTempPath(), "agent-packs-" + Guid.NewGuid().ToString("N"));
            CopyDirectory(sourceRoot, root);
            return new TempPackRoot(root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, target, StringComparison.Ordinal));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var dest = file.Replace(source, target, StringComparison.Ordinal);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
        }
    }
}
