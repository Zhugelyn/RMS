using AssistantApi.Contracts;
using AssistantApi.Providers;

namespace AssistantApi.Services;

public sealed class ChatService
{
    private readonly ILlmProvider _llmProvider;
    private readonly ILogger<ChatService> _logger;

    public ChatService(ILlmProvider llmProvider, ILogger<ChatService> logger)
    {
        _llmProvider = llmProvider;
        _logger = logger;
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Chat request conversationId={ConversationId} userId={UserId} traceId={TraceId} intent={Intent} resume={Resume} provider={Provider}",
            request.ConversationId,
            request.UserId,
            request.TraceId,
            request.Intent?.ToString() ?? "none",
            !string.IsNullOrWhiteSpace(request.AgentId),
            _llmProvider.Name);

        return await _llmProvider.CompleteAsync(request, cancellationToken);
    }
}
