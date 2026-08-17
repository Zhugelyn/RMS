using AssistantApi.Contracts;
using AssistantApi.Harness;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Providers;
using AssistantApi.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

public sealed class HarnessAndSecretTests
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

            var outputCopy = Path.Combine(dir.FullName, "AgentPacks");
            if (Directory.Exists(Path.Combine(outputCopy, "salon")))
            {
                return outputCopy;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("AgentPacks not found");
    }

    private static IPackCatalog Catalog() => new PackCatalog(FindPacksRoot());

    [Theory]
    [InlineData(ChatIntent.Salon, DomainIntent.Salon)]
    [InlineData(ChatIntent.Marketing, DomainIntent.Marketing)]
    [InlineData(ChatIntent.Tasks, DomainIntent.Tasks)]
    public void Classify_uses_explicit_intent(ChatIntent chatIntent, DomainIntent expected)
    {
        var harness = new DomainHarness(Catalog());
        var intent = harness.Classify(new ChatRequest
        {
            Text = "hello",
            Intent = chatIntent,
            ConversationId = "c",
            UserId = "u",
            TraceId = "t",
            SchemaVersion = 1
        });
        Assert.Equal(expected, intent);
    }

    [Fact]
    public void Classify_infers_salon_babor_from_text()
    {
        var harness = new DomainHarness(Catalog());
        var intent = harness.Classify(new ChatRequest
        {
            Text = "Как развивать салон Babor в Брянске?",
            ConversationId = "c",
            UserId = "u",
            TraceId = "t",
            SchemaVersion = 1
        });
        Assert.Equal(DomainIntent.Salon, intent);
    }

    [Fact]
    public void Verify_rejects_secret_leak()
    {
        var harness = new DomainHarness(Catalog());
        var result = harness.Verify(DomainIntent.Tasks, "use CURSOR_API_KEY=sk-abcdefghijklmnopqrstuvwxyz");
        Assert.False(result.Ok);
        Assert.Equal("secret-leak", result.Reason);
    }

    [Fact]
    public void Verify_rejects_domain_drift_for_salon()
    {
        var harness = new DomainHarness(Catalog());
        var result = harness.Verify(
            DomainIntent.Salon,
            "Доля рынка красоты в целом растёт. Топ бренд без привязки. Таргет аудитории 18-24.");
        Assert.False(result.Ok);
        Assert.Equal("domain-drift", result.Reason);
    }

    [Fact]
    public void AesGcm_roundtrip_does_not_echo_plaintext_in_blob()
    {
        var options = MsOptions.Create(new CursorOptions
        {
            MasterKey = "test-master-key-32chars-min!!"
        });
        var protector = new AesGcmSecretProtector(options);
        const string secret = "sk-test-cursor-api-key-value";
        var blob = protector.Protect(secret);
        Assert.DoesNotContain(secret, blob, StringComparison.Ordinal);
        Assert.Equal(secret, protector.Unprotect(blob));
    }

    [Fact]
    public async Task CursorSdk_provider_uses_pack_and_affinity_per_domain()
    {
        var catalog = Catalog();
        var client = new FakeCursorClient();
        var affinity = new InMemoryAgentAffinityStore();
        var memory = new InMemoryHarnessMemoryStore();
        var provider = CreateProvider(catalog, client, affinity, memory);

        var first = await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c1",
            UserId = "u1",
            Text = "идеи для удержания клиентов Babor",
            TraceId = "t1",
            Intent = ChatIntent.Salon
        }, CancellationToken.None);

        Assert.Equal("cursor-sdk", first.Provider);
        Assert.Equal("agent-salon-1", first.AgentId);
        Assert.Equal(ChatIntent.Salon, first.Intent);
        Assert.Equal(PackIds.Salon, first.DomainPack);
        Assert.Equal(PackIds.Salon, client.Calls[0].PackId);
        Assert.Contains("Babor", client.Calls[0].Prompt, StringComparison.OrdinalIgnoreCase);

        var second = await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c1",
            UserId = "u1",
            Text = "уточни оффер",
            TraceId = "t2",
            Intent = ChatIntent.Salon
        }, CancellationToken.None);

        Assert.Equal("agent-salon-1", second.AgentId);
        Assert.Equal("agent-salon-1", client.Calls[1].AgentId);

        // Cross-domain: marketing must not resume salon agentId
        client.Calls.Clear();
        var marketing = await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c1",
            UserId = "u1",
            Text = "какие бренды косметики сейчас в тренде",
            TraceId = "t3",
            Intent = ChatIntent.Marketing,
            AgentId = "agent-salon-1"
        }, CancellationToken.None);

        Assert.Equal(PackIds.Marketing, marketing.DomainPack);
        Assert.Null(client.Calls[0].AgentId);
        Assert.Equal(PackIds.Marketing, client.Calls[0].PackId);
        Assert.Equal("agent-marketing-1", marketing.AgentId);
    }

    [Fact]
    public async Task Memory_episodes_are_isolated_per_domain()
    {
        var catalog = Catalog();
        var client = new FakeCursorClient();
        var memory = new InMemoryHarnessMemoryStore();
        var provider = CreateProvider(catalog, client, new InMemoryAgentAffinityStore(), memory);

        await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c-mem",
            UserId = "u-mem",
            Text = "факт: у Babor 3 мастера",
            TraceId = "t1",
            Intent = ChatIntent.Salon
        }, CancellationToken.None);

        await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c-mem",
            UserId = "u-mem",
            Text = "какие бренды любит аудитория 25-34",
            TraceId = "t2",
            Intent = ChatIntent.Marketing
        }, CancellationToken.None);

        var salonEps = await memory.GetRecentEpisodesAsync("u-mem", PackIds.Salon, 10, CancellationToken.None);
        var marketingEps = await memory.GetRecentEpisodesAsync("u-mem", PackIds.Marketing, 10, CancellationToken.None);
        Assert.Single(salonEps);
        Assert.Single(marketingEps);
        Assert.Contains("Babor", salonEps[0].Task, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Babor", marketingEps[0].Task, StringComparison.OrdinalIgnoreCase);

        // Next salon prompt should include salon episode, not marketing
        client.Calls.Clear();
        await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c-mem",
            UserId = "u-mem",
            Text = "что мы уже знаем о салоне?",
            TraceId = "t3",
            Intent = ChatIntent.Salon
        }, CancellationToken.None);

        Assert.Contains("3 мастера", client.Calls[0].Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("бренды любит", client.Calls[0].Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pack_isolation_skills_dirs_do_not_overlap()
    {
        var catalog = Catalog();
        var salon = catalog.GetRequired(PackIds.Salon);
        var marketing = catalog.GetRequired(PackIds.Marketing);
        Assert.NotEqual(salon.DirectoryPath, marketing.DirectoryPath);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar + "marketing" + Path.DirectorySeparatorChar,
            salon.SkillsDirectoryPath + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.Combine(salon.DirectoryPath, ".cursor", "skills")));
        Assert.False(File.Exists(Path.Combine(salon.SkillsDirectoryPath, "marketing-basics", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(marketing.SkillsDirectoryPath, "marketing-basics", "SKILL.md")));
    }

    [Fact]
    public async Task Fallback_uses_stub_without_key()
    {
        var catalog = Catalog();
        var harness = new DomainHarness(catalog);
        var cursor = new CursorSdkLlmProvider(
            new FakeKeyStore(null),
            harness,
            new FakeCursorClient(),
            catalog,
            new PackPromptBuilder(),
            new InMemoryAgentAffinityStore(),
            new InMemoryHarnessMemoryStore(),
            MsOptions.Create(new CursorOptions()),
            NullLogger<CursorSdkLlmProvider>.Instance);

        var fallback = new FallbackLlmProvider(
            cursor,
            new StubLlmProvider(harness),
            NullLogger<FallbackLlmProvider>.Instance);

        var response = await fallback.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c1",
            UserId = "u1",
            Text = "привет",
            TraceId = "t1",
            Intent = ChatIntent.Salon
        }, CancellationToken.None);

        Assert.Equal("stub", response.Provider);
        Assert.Equal(PackIds.Salon, response.DomainPack);
        Assert.Contains("Babor", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static CursorSdkLlmProvider CreateProvider(
        IPackCatalog catalog,
        FakeCursorClient client,
        IAgentAffinityStore affinity,
        IHarnessMemoryStore memory) =>
        new(
            new FakeKeyStore("sk-test-key-not-real-xxxxxx"),
            new DomainHarness(catalog),
            client,
            catalog,
            new PackPromptBuilder(),
            affinity,
            memory,
            MsOptions.Create(new CursorOptions { Model = "composer-2.5" }),
            NullLogger<CursorSdkLlmProvider>.Instance);

    private sealed class FakeKeyStore : ICursorApiKeyStore
    {
        private readonly string? _key;
        public FakeKeyStore(string? key) => _key = key;
        public bool HasKey => !string.IsNullOrEmpty(_key);
        public bool TryGetApiKey(out string apiKey)
        {
            apiKey = _key ?? string.Empty;
            return HasKey;
        }
    }

    private sealed class FakeCursorClient : ICursorSdkClient
    {
        public List<CursorSdkRunRequest> Calls { get; } = new();
        private int _salon;
        private int _marketing;
        private int _tasks;
        private int _router;

        public Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            var pack = request.PackId ?? "none";
            string id;
            string text;
            if (pack == PackIds.Router)
            {
                _router++;
                id = $"agent-router-{_router}";
                text = "salon";
            }
            else if (pack == PackIds.Salon)
            {
                _salon++;
                id = string.IsNullOrWhiteSpace(request.AgentId) ? $"agent-salon-{_salon}" : request.AgentId!;
                text = "Ок, для Babor в Брянске предлагаю шаг по удержанию клиентов салона.";
            }
            else if (pack == PackIds.Marketing)
            {
                _marketing++;
                id = string.IsNullOrWhiteSpace(request.AgentId) ? $"agent-marketing-{_marketing}" : request.AgentId!;
                text = "По рынку красоты: гипотеза по аудитории и таргету без секретов.";
            }
            else
            {
                _tasks++;
                id = string.IsNullOrWhiteSpace(request.AgentId) ? $"agent-tasks-{_tasks}" : request.AgentId!;
                text = "План: слот завтра 10:00 и напоминание за час.";
            }

            return Task.FromResult(new CursorSdkRunResult(id, text, pack));
        }
    }
}
