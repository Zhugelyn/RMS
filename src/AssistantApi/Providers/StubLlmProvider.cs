using AssistantApi.Contracts;
using AssistantApi.Harness;
using AssistantApi.Packs;

namespace AssistantApi.Providers;

public sealed class StubLlmProvider : ILlmProvider
{
    private readonly IDomainHarness _harness;

    public StubLlmProvider(IDomainHarness harness)
    {
        _harness = harness;
    }

    public string Name => "stub";

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var intent = _harness.Classify(request);
        var packId = DomainHarness.ToPackId(intent);
        var intentLabel = intent.ToString().ToLowerInvariant();
        var text = intent switch
        {
            DomainIntent.Salon =>
                $"[{Name}/salon] Babor (Брянск): принял «{request.Text.Trim()}». " +
                "Stub: без Cursor key. Могу предлагать идеи развития салона, когда подключите SDK.",
            DomainIntent.Marketing =>
                $"[{Name}/marketing] Рынок красоты: принял «{request.Text.Trim()}». " +
                "Stub: анализ брендов/аудиторий/таргета будет через Cursor pack.",
            DomainIntent.Tasks =>
                $"[{Name}/tasks] Расписание: принял «{request.Text.Trim()}». " +
                "Stub: план слотов и напоминаний через Cursor pack.",
            _ =>
                $"[{Name}/{intentLabel}] Принял: {request.Text.Trim()}. Stub fallback без Cursor API key."
        };

        var response = new ChatResponse
        {
            SchemaVersion = 1,
            ConversationId = request.ConversationId,
            MessageId = Guid.NewGuid().ToString("N"),
            Text = text,
            Provider = Name,
            AgentId = request.AgentId,
            Intent = ToChatIntent(intent),
            DomainPack = packId
        };

        return Task.FromResult(response);
    }

    private static ChatIntent ToChatIntent(DomainIntent intent) => intent switch
    {
        DomainIntent.Salon => ChatIntent.Salon,
        DomainIntent.Marketing => ChatIntent.Marketing,
        DomainIntent.Tasks => ChatIntent.Tasks,
        _ => ChatIntent.General
    };
}
