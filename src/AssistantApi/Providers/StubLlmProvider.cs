using AssistantApi.Contracts;

namespace AssistantApi.Providers;

public sealed class StubLlmProvider : ILlmProvider
{
    public string Name => "stub";

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var intentLabel = request.Intent?.ToString().ToLowerInvariant() ?? "general";
        var text =
            $"[{Name}/{intentLabel}] Принял: {request.Text.Trim()}. " +
            "Это stub-ответ Phase 1 без внешнего LLM.";

        var response = new ChatResponse
        {
            SchemaVersion = 1,
            ConversationId = request.ConversationId,
            MessageId = Guid.NewGuid().ToString("N"),
            Text = text,
            Provider = Name
        };

        return Task.FromResult(response);
    }
}
