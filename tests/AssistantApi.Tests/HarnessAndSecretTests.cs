using AssistantApi.Contracts;
using AssistantApi.Harness;
using AssistantApi.Options;
using AssistantApi.Providers;
using AssistantApi.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

public sealed class HarnessAndSecretTests
{
    [Theory]
    [InlineData(ChatIntent.Salon, DomainIntent.Salon)]
    [InlineData(ChatIntent.Marketing, DomainIntent.Marketing)]
    [InlineData(ChatIntent.Tasks, DomainIntent.Tasks)]
    public void Classify_uses_explicit_intent(ChatIntent chatIntent, DomainIntent expected)
    {
        var harness = new DomainHarness();
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
    public void Classify_infers_salon_from_text()
    {
        var harness = new DomainHarness();
        var intent = harness.Classify(new ChatRequest
        {
            Text = "Нужна запись к мастеру на маникюр",
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
        var harness = new DomainHarness();
        var result = harness.Verify(DomainIntent.Tasks, "use CURSOR_API_KEY=sk-abcdefghijklmnopqrstuvwxyz");
        Assert.False(result.Ok);
        Assert.Equal("secret-leak", result.Reason);
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
    public async Task CursorSdk_provider_resumes_by_agentId()
    {
        var store = new FakeKeyStore("sk-test-key-not-real-xxxxxx");
        var client = new FakeCursorClient();
        var provider = new CursorSdkLlmProvider(
            store,
            new DomainHarness(),
            client,
            MsOptions.Create(new CursorOptions { Model = "composer-2.5" }),
            NullLogger<CursorSdkLlmProvider>.Instance);

        var first = await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c1",
            UserId = "u1",
            Text = "прайс на рекламу",
            TraceId = "t1",
            Intent = ChatIntent.Marketing
        }, CancellationToken.None);

        Assert.Equal("cursor-sdk", first.Provider);
        Assert.Equal("agent-1", first.AgentId);
        Assert.Equal(ChatIntent.Marketing, first.Intent);

        var second = await provider.CompleteAsync(new ChatRequest
        {
            SchemaVersion = 1,
            ConversationId = "c1",
            UserId = "u1",
            Text = "уточни бюджет",
            TraceId = "t2",
            Intent = ChatIntent.Marketing,
            AgentId = first.AgentId
        }, CancellationToken.None);

        Assert.Equal("agent-1", second.AgentId);
        Assert.Equal(2, client.Calls.Count);
        Assert.Null(client.Calls[0].AgentId);
        Assert.Equal("agent-1", client.Calls[1].AgentId);
        Assert.Contains("Маркетинг", client.Calls[0].Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fallback_uses_stub_without_key()
    {
        var cursor = new CursorSdkLlmProvider(
            new FakeKeyStore(null),
            new DomainHarness(),
            new FakeCursorClient(),
            MsOptions.Create(new CursorOptions()),
            NullLogger<CursorSdkLlmProvider>.Instance);

        var fallback = new FallbackLlmProvider(
            cursor,
            new StubLlmProvider(),
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
        Assert.Contains("salon", response.Text, StringComparison.OrdinalIgnoreCase);
    }

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

        public Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            var id = string.IsNullOrWhiteSpace(request.AgentId) ? "agent-1" : request.AgentId!;
            return Task.FromResult(new CursorSdkRunResult(id, $"ok:{request.Prompt.Length}"));
        }
    }
}
