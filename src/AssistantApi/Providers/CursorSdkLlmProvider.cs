using AssistantApi.Contracts;
using AssistantApi.Harness;
using AssistantApi.Options;
using AssistantApi.Security;
using Microsoft.Extensions.Options;

namespace AssistantApi.Providers;

public sealed class CursorSdkLlmProvider : ILlmProvider
{
    private readonly ICursorApiKeyStore _keyStore;
    private readonly IDomainHarness _harness;
    private readonly ICursorSdkClient _sdkClient;
    private readonly CursorOptions _options;
    private readonly ILogger<CursorSdkLlmProvider> _logger;

    public CursorSdkLlmProvider(
        ICursorApiKeyStore keyStore,
        IDomainHarness harness,
        ICursorSdkClient sdkClient,
        IOptions<CursorOptions> options,
        ILogger<CursorSdkLlmProvider> logger)
    {
        _keyStore = keyStore;
        _harness = harness;
        _sdkClient = sdkClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "cursor-sdk";

    public bool CanHandle => _keyStore.HasKey;

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (!_keyStore.TryGetApiKey(out var apiKey))
        {
            throw new InvalidOperationException("Cursor API key is not configured.");
        }

        try
        {
            var intent = _harness.Classify(request);
            var prompt = _harness.BuildSpecialistPrompt(intent, request);

            _logger.LogInformation(
                "Harness classify→agent conversationId={ConversationId} intent={Intent} resume={Resume}",
                request.ConversationId,
                intent,
                !string.IsNullOrWhiteSpace(request.AgentId));

            var run = await _sdkClient.RunAsync(
                new CursorSdkRunRequest(
                    ApiKey: apiKey,
                    Prompt: prompt,
                    AgentId: string.IsNullOrWhiteSpace(request.AgentId) ? null : request.AgentId,
                    Model: _options.Model),
                cancellationToken);

            var verified = _harness.Verify(intent, run.Text);
            if (!verified.Ok)
            {
                _logger.LogWarning(
                    "Harness verify failed reason={Reason} conversationId={ConversationId}",
                    verified.Reason,
                    request.ConversationId);

                // One repair pass without resume (fresh classify context).
                var repairPrompt = prompt + "\n\nПредыдущий ответ отклонён verify (" + verified.Reason +
                                   "). Ответь кратко по домену без секретов.";
                run = await _sdkClient.RunAsync(
                    new CursorSdkRunRequest(apiKey, repairPrompt, run.AgentId, _options.Model),
                    cancellationToken);
                verified = _harness.Verify(intent, run.Text);
                if (!verified.Ok)
                {
                    throw new InvalidOperationException($"Harness verify failed: {verified.Reason}");
                }
            }

            return new ChatResponse
            {
                SchemaVersion = 1,
                ConversationId = request.ConversationId,
                MessageId = Guid.NewGuid().ToString("N"),
                Text = verified.Text,
                Provider = Name,
                AgentId = run.AgentId,
                Intent = ToChatIntent(intent)
            };
        }
        finally
        {
            apiKey = string.Empty;
        }
    }

    private static ChatIntent ToChatIntent(DomainIntent intent) => intent switch
    {
        DomainIntent.Salon => ChatIntent.Salon,
        DomainIntent.Marketing => ChatIntent.Marketing,
        DomainIntent.Tasks => ChatIntent.Tasks,
        _ => ChatIntent.General
    };
}
