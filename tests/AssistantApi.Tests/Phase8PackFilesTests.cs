using AssistantApi.Contracts;
using AssistantApi.Files;
using AssistantApi.Harness;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Providers;
using AssistantApi.Rag;
using AssistantApi.Research;
using AssistantApi.Security;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

/// <summary>
/// phase8-pack-files: salon/marketing MCP files + skill; router/tasks empty;
/// domain prefix isolation; soft-fail; no Direct/Apify/byte proxy.
/// </summary>
public sealed class Phase8PackFilesTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
    }

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

            var outputCopy = Path.Combine(dir.FullName, "AgentPacks");
            if (Directory.Exists(Path.Combine(outputCopy, "salon")))
            {
                return outputCopy;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("AgentPacks not found");
    }

    [Fact]
    public void Packs_salon_marketing_have_files_mcp_router_tasks_do_not()
    {
        var catalog = new PackCatalog(FindPacksRoot());
        Assert.Equal(
            new[] { RagMcp.KbRetriever, FileMcp.Files },
            catalog.GetRequired(PackIds.Salon).Mcp.Allowlist);
        Assert.Equal(
            new[] { RagMcp.KbRetriever, FileMcp.Files },
            catalog.GetRequired(PackIds.Marketing).Mcp.Allowlist);
        Assert.Contains(
            FileMcp.Files,
            catalog.GetRequired(PackIds.Salon).Mcp.Servers.Select(s => s.Name));
        Assert.Contains(
            FileMcp.Files,
            catalog.GetRequired(PackIds.Marketing).Mcp.Servers.Select(s => s.Name));
        Assert.Empty(catalog.GetRequired(PackIds.Tasks).Mcp.Allowlist);
        Assert.Empty(catalog.GetRequired(PackIds.Router).Mcp.Allowlist);

        Assert.True(File.Exists(Path.Combine(
            catalog.GetRequired(PackIds.Salon).SkillsDirectoryPath, "files-store", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(
            catalog.GetRequired(PackIds.Marketing).SkillsDirectoryPath, "files-store", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(
            catalog.GetRequired(PackIds.Tasks).SkillsDirectoryPath, "files-store", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(
            catalog.GetRequired(PackIds.Router).SkillsDirectoryPath, "files-store", "SKILL.md")));
    }

    [Fact]
    public async Task Injector_salon_gets_own_files_marketing_isolation_tasks_null()
    {
        var store = new InMemoryFileObjectStore();
        var salonId = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var mktId = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        await store.InsertAsync(ActiveFile(salonId, "tg-42", FileDomains.Salon, "tg-ai-salon", "SALON_FILE_MARKER.jpg"), CancellationToken.None);
        await store.InsertAsync(ActiveFile(mktId, "tg-42", FileDomains.Marketing, "tg-ai-marketing", "MKT_FILE_MARKER.png"), CancellationToken.None);

        var injector = new FilesPackInjector(store, NullLogger<FilesPackInjector>.Instance);

        var salon = await injector.BuildInjectBlockAsync("tg-42", PackIds.Salon, CancellationToken.None);
        Assert.NotNull(salon);
        Assert.Contains(salonId.ToString("N"), salon, StringComparison.Ordinal);
        Assert.Contains(FileMcp.Files, salon, StringComparison.Ordinal);
        Assert.Contains("SALON_FILE_MARKER", salon, StringComparison.Ordinal);
        Assert.DoesNotContain(mktId.ToString("N"), salon, StringComparison.Ordinal);
        Assert.DoesNotContain("MKT_FILE_MARKER", salon, StringComparison.Ordinal);
        Assert.DoesNotContain("tg-ai-", salon, StringComparison.Ordinal); // no bucket leak
        Assert.DoesNotContain("http://", salon, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", salon, StringComparison.OrdinalIgnoreCase);

        var marketing = await injector.BuildInjectBlockAsync("tg-42", PackIds.Marketing, CancellationToken.None);
        Assert.NotNull(marketing);
        Assert.Contains(mktId.ToString("N"), marketing, StringComparison.Ordinal);
        Assert.DoesNotContain(salonId.ToString("N"), marketing, StringComparison.Ordinal);

        Assert.Null(await injector.BuildInjectBlockAsync("tg-42", PackIds.Tasks, CancellationToken.None));
        Assert.Null(await injector.BuildInjectBlockAsync("tg-42", PackIds.Router, CancellationToken.None));
        Assert.Null(await injector.BuildInjectBlockAsync("tg-999", PackIds.Salon, CancellationToken.None));
    }

    [Fact]
    public async Task Soft_fail_throwing_store_does_not_break_chat()
    {
        var catalog = new PackCatalog(FindPacksRoot());
        var client = new RecordingClient();
        var provider = CreateProvider(
            catalog,
            client,
            new FilesPackInjector(new ThrowingFileStore(), NullLogger<FilesPackInjector>.Instance));

        var survived = await provider.CompleteAsync(Sample(ChatIntent.Salon, "привет"), CancellationToken.None);
        Assert.Equal("cursor-sdk", survived.Provider);
        Assert.Equal(PackIds.Salon, survived.DomainPack);
        Assert.False(string.IsNullOrWhiteSpace(survived.Text));
    }

    [Fact]
    public async Task Cursor_prompt_includes_files_for_salon_not_tasks()
    {
        var store = new InMemoryFileObjectStore();
        var fileId = Guid.Parse("cccccccccccccccccccccccccccccccc");
        await store.InsertAsync(
            ActiveFile(fileId, "tg-77", FileDomains.Salon, "tg-ai-salon", "prompt-marker.pdf"),
            CancellationToken.None);

        var catalog = new PackCatalog(FindPacksRoot());
        var client = new RecordingClient();
        var injector = new FilesPackInjector(store, NullLogger<FilesPackInjector>.Instance);
        var provider = CreateProvider(catalog, client, injector);

        await provider.CompleteAsync(Sample(ChatIntent.Salon, "какие файлы есть?", "tg-77"), CancellationToken.None);
        Assert.Contains(fileId.ToString("N"), client.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("User files", client.LastPrompt, StringComparison.Ordinal);

        client.LastPrompt = "";
        await provider.CompleteAsync(Sample(ChatIntent.Tasks, "поставь напоминание", "tg-77"), CancellationToken.None);
        Assert.DoesNotContain(fileId.ToString("N"), client.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("User files", client.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_recent_filters_pending_and_other_domain()
    {
        var store = new InMemoryFileObjectStore();
        var active = Guid.NewGuid();
        var pending = Guid.NewGuid();
        await store.InsertAsync(ActiveFile(active, "tg-1", FileDomains.Salon, "tg-ai-salon", "ok.jpg"), CancellationToken.None);
        await store.InsertAsync(new FileObject
        {
            FileId = pending,
            UserId = "tg-1",
            Domain = FileDomains.Salon,
            Bucket = "tg-ai-salon",
            ObjectKey = pending.ToString("N"),
            OriginalFilename = "pending.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 10,
            Status = FileStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        await store.InsertAsync(
            ActiveFile(Guid.NewGuid(), "tg-1", FileDomains.Marketing, "tg-ai-marketing", "other.png"),
            CancellationToken.None);

        var list = await store.ListRecentAsync("tg-1", FileDomains.Salon, 10, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal(active, list[0].FileId);
    }

    [Fact]
    public void Assistant_api_has_no_direct_apify_or_byte_proxy_helpers()
    {
        var repoRoot = FindRepoRoot();
        var csproj = File.ReadAllText(Path.Combine(repoRoot, "src", "AssistantApi", "AssistantApi.csproj"));
        Assert.DoesNotContain("Apify", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yandex.Direct", csproj, StringComparison.OrdinalIgnoreCase);

        var filesDir = Path.Combine(repoRoot, "src", "AssistantApi", "Files");
        foreach (var file in Directory.EnumerateFiles(filesDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            // Presigned*ObjectAsync OK; raw GetObject/PutObject byte proxy forbidden.
            Assert.DoesNotContain(".GetObjectAsync(", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".PutObjectAsync(", text, StringComparison.Ordinal);
            Assert.DoesNotContain("yandex-direct", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("apify", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static CursorSdkLlmProvider CreateProvider(
        IPackCatalog catalog,
        RecordingClient client,
        IFilesPackInjector filesInject) =>
        new(
            new FixedKeyStore("sk-test-key-not-real-xxxxxx"),
            new DomainHarness(catalog),
            client,
            catalog,
            new PackPromptBuilder(),
            new InMemoryAgentAffinityStore(),
            new InMemoryHarnessMemoryStore(),
            new ResearchPackInjector(new InMemoryResearchArtifactStore()),
            new NoOpRagPackInjector(),
            filesInject,
            MsOptions.Create(new CursorOptions { Model = "composer-2.5" }),
            NullLogger<CursorSdkLlmProvider>.Instance);

    private static ChatRequest Sample(ChatIntent intent, string text, string userId = "tg-42") => new()
    {
        SchemaVersion = 1,
        ConversationId = "c-files",
        UserId = userId,
        Text = text,
        TraceId = "t-files",
        Intent = intent
    };

    private static FileObject ActiveFile(
        Guid id, string userId, string domain, string bucket, string filename) => new()
    {
        FileId = id,
        UserId = userId,
        Domain = domain,
        Bucket = bucket,
        ObjectKey = id.ToString("N"),
        OriginalFilename = filename,
        ContentType = "image/jpeg",
        SizeBytes = 1024,
        Status = FileStatuses.Active,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        UploadedAt = DateTimeOffset.UtcNow.AddMinutes(-4)
    };

    private sealed class ThrowingFileStore : IFileObjectStore
    {
        public Task InsertAsync(FileObject file, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated store outage");

        public Task<FileObject?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated store outage");

        public Task UpdateAsync(FileObject file, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated store outage");

        public Task<IReadOnlyList<FileObject>> ListRecentAsync(
            string userId, string domain, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated store outage");
    }

    private sealed class FixedKeyStore : ICursorApiKeyStore
    {
        private readonly string _key;
        public FixedKeyStore(string key) => _key = key;
        public bool HasKey => true;
        public bool TryGetApiKey(out string apiKey)
        {
            apiKey = _key;
            return true;
        }
    }

    private sealed class RecordingClient : ICursorSdkClient
    {
        public string LastPrompt { get; set; } = "";
        private int _n;

        public Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken)
        {
            LastPrompt = request.Prompt;
            _n++;
            var pack = request.PackId ?? "salon";
            return Task.FromResult(new CursorSdkRunResult(
                agentId: $"agent-{pack}-{_n}",
                text: pack switch
                {
                    "marketing" => "marketing specialist reply about beauty market trends",
                    "tasks" => "tasks specialist reply about schedule reminder",
                    _ => "salon specialist reply about Babor Bryansk growth"
                }));
        }
    }
}
