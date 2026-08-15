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
            "Это stub-ответ (fallback без Cursor API key / при ошибке SDK).";

        var response = new ChatResponse
        {
            SchemaVersion = 1,
            ConversationId = request.ConversationId,
            MessageId = Guid.NewGuid().ToString("N"),
            Text = text,
            Provider = Name,
            AgentId = request.AgentId,
            Intent = request.Intent
        };

        return Task.FromResult(response);
    }
}
