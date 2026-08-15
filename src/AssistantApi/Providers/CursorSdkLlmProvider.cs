using AssistantApi.Contracts;
using AssistantApi.Harness;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Security;
using Microsoft.Extensions.Options;

namespace AssistantApi.Providers;

public sealed class CursorSdkLlmProvider : ILlmProvider
{
    private readonly ICursorApiKeyStore _keyStore;
    private readonly IDomainHarness _harness;
    private readonly ICursorSdkClient _sdkClient;
    private readonly IPackCatalog _packs;
    private readonly IPackPromptBuilder _prompts;
    private readonly IAgentAffinityStore _affinity;
    private readonly IHarnessMemoryStore _memory;
    private readonly CursorOptions _options;
    private readonly ILogger<CursorSdkLlmProvider> _logger;

    public CursorSdkLlmProvider(
        ICursorApiKeyStore keyStore,
        IDomainHarness harness,
        ICursorSdkClient sdkClient,
        IPackCatalog packs,
        IPackPromptBuilder prompts,
        IAgentAffinityStore affinity,
        IHarnessMemoryStore memory,
        IOptions<CursorOptions> options,
        ILogger<CursorSdkLlmProvider> logger)
    {
        _keyStore = keyStore;
        _harness = harness;
        _sdkClient = sdkClient;
        _packs = packs;
        _prompts = prompts;
        _affinity = affinity;
        _memory = memory;
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
            var intent = await ResolveIntentAsync(apiKey, request, cancellationToken);
            var packId = DomainHarness.ToPackId(intent);
            var pack = _packs.GetRequired(packId);
            if (!pack.Manifest.AnswersUser)
            {
                throw new InvalidOperationException($"Pack '{packId}' cannot answer users.");
            }

            var profile = await _memory.GetProfileAsync(request.UserId, cancellationToken);
            var episodes = await _memory.GetRecentEpisodesAsync(request.UserId, packId, limit: 5, cancellationToken);
            var memoryBlock = _prompts.BuildMemoryBlock(profile, episodes);
            var prompt = _prompts.BuildSpecialistPrompt(pack, request, memoryBlock);

            // Affinity is source of truth: never resume another domain's agentId from the client.
            string? resumeId = null;
            if (_affinity.TryGet(request.ConversationId, packId, out var affinityId))
            {
                resumeId = affinityId;
            }

            _logger.LogInformation(
                "Pack harness conversationId={ConversationId} pack={PackId} intent={Intent} resume={Resume}",
                request.ConversationId,
                packId,
                intent,
                !string.IsNullOrWhiteSpace(resumeId));

            var model = string.IsNullOrWhiteSpace(pack.Manifest.Model) ? _options.Model : pack.Manifest.Model!;
            var run = await _sdkClient.RunAsync(
                new CursorSdkRunRequest(apiKey, prompt, resumeId, model, packId),
                cancellationToken);

            var verified = _harness.Verify(intent, run.Text);
            if (!verified.Ok)
            {
                _logger.LogWarning(
                    "Hard verify failed reason={Reason} conversationId={ConversationId} pack={PackId}",
                    verified.Reason,
                    request.ConversationId,
                    packId);

                var repairPrompt = prompt +
                                   "\n\nПредыдущий ответ отклонён verify (" + verified.Reason +
                                   "). Ответь строго в рамках domain pack без секретов и без drift.";
                run = await _sdkClient.RunAsync(
                    new CursorSdkRunRequest(apiKey, repairPrompt, run.AgentId, model, packId),
                    cancellationToken);
                verified = _harness.Verify(intent, run.Text);
                if (!verified.Ok)
                {
                    throw new InvalidOperationException($"Harness verify failed: {verified.Reason}");
                }
            }

            _affinity.Set(request.ConversationId, packId, run.AgentId);

            try
            {
                await _memory.AddEpisodeAsync(new HarnessEpisode
                {
                    UserId = request.UserId,
                    Domain = packId,
                    Task = Trim(request.Text, 180),
                    Result = Trim(verified.Text, 220),
                    At = DateTimeOffset.UtcNow,
                    ConversationId = request.ConversationId,
                    TraceId = request.TraceId
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Harness episode persist failed conversationId={ConversationId}", request.ConversationId);
            }

            return new ChatResponse
            {
                SchemaVersion = 1,
                ConversationId = request.ConversationId,
                MessageId = Guid.NewGuid().ToString("N"),
                Text = verified.Text,
                Provider = Name,
                AgentId = run.AgentId,
                Intent = ToChatIntent(intent),
                DomainPack = packId
            };
        }
        finally
        {
            apiKey = string.Empty;
        }
    }

    private async Task<DomainIntent> ResolveIntentAsync(string apiKey, ChatRequest request, CancellationToken cancellationToken)
    {
        if (request.Intent is not null)
        {
            return _harness.Classify(request);
        }

        // Router pack (SDK) — not keyword-only when Cursor path is live.
        try
        {
            var router = _packs.GetRequired(PackIds.Router);
            var profile = await _memory.GetProfileAsync(request.UserId, cancellationToken);
            var profileHint = profile is null
                ? null
                : _prompts.BuildMemoryBlock(profile, Array.Empty<HarnessEpisode>(), maxChars: 400);
            var routerPrompt = _prompts.BuildRouterPrompt(router, request, profileHint);
            var model = string.IsNullOrWhiteSpace(router.Manifest.Model) ? _options.Model : router.Manifest.Model!;
            var routed = await _sdkClient.RunAsync(
                new CursorSdkRunRequest(apiKey, routerPrompt, AgentId: null, model, PackIds.Router),
                cancellationToken);
            return DomainHarness.ParseRouterLabel(routed.Text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Router pack failed; falling back to pack-hint classify");
            return _harness.Classify(request);
        }
    }

    private static ChatIntent ToChatIntent(DomainIntent intent) => intent switch
    {
        DomainIntent.Salon => ChatIntent.Salon,
        DomainIntent.Marketing => ChatIntent.Marketing,
        DomainIntent.Tasks => ChatIntent.Tasks,
        _ => ChatIntent.General
    };

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";
}
