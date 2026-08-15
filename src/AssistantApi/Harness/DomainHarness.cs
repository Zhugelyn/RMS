using System.Text;
using AssistantApi.Contracts;

namespace AssistantApi.Harness;

/// <summary>Phase 2 harness: classify → specialist agent prompt → verify.</summary>
public sealed class DomainHarness : IDomainHarness
{
    public DomainIntent Classify(ChatRequest request)
    {
        if (request.Intent is ChatIntent.Salon)
        {
            return DomainIntent.Salon;
        }

        if (request.Intent is ChatIntent.Marketing)
        {
            return DomainIntent.Marketing;
        }

        if (request.Intent is ChatIntent.Tasks)
        {
            return DomainIntent.Tasks;
        }

        var text = request.Text.ToLowerInvariant();
        if (ContainsAny(text, "салон", "запись", "мастер", "стрижк", "маникюр", "клиент", "расписан"))
        {
            return DomainIntent.Salon;
        }

        if (ContainsAny(text, "маркетинг", "реклам", "директ", "кампан", "лид", "конверс", "ads"))
        {
            return DomainIntent.Marketing;
        }

        if (ContainsAny(text, "задач", "планир", "встреч", "todo", "календар", "напомин"))
        {
            return DomainIntent.Tasks;
        }

        return DomainIntent.General;
    }

    public string BuildSpecialistPrompt(DomainIntent intent, ChatRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ты specialist-агент Telegram AI. Короткий ответ на русском, без секретов и без кода.");
        sb.AppendLine(intent switch
        {
            DomainIntent.Salon => "Домен: салон красоты (записи, расписание, клиенты). Не выдумывай реальные брони без данных.",
            DomainIntent.Marketing => "Домен: маркетинг (реклама, мониторинг, монетизация). Не проси API keys.",
            DomainIntent.Tasks => "Домен: повседневные задачи (планирование, встречи). Дай конкретные следующие шаги.",
            _ => "Домен: general. Уточни intent, если запрос про салон/маркетинг/задачи."
        });
        sb.AppendLine($"conversationId={request.ConversationId}");
        sb.AppendLine($"userId={request.UserId}");
        sb.AppendLine($"traceId={request.TraceId}");
        sb.AppendLine("Запрос пользователя:");
        sb.Append(request.Text.Trim());
        return sb.ToString();
    }

    public HarnessVerifyResult Verify(DomainIntent intent, string specialistOutput)
    {
        if (string.IsNullOrWhiteSpace(specialistOutput))
        {
            return new HarnessVerifyResult(false, string.Empty, "empty");
        }

        var text = specialistOutput.Trim();
        if (text.Length > 4000)
        {
            text = text[..4000].TrimEnd() + "…";
        }

        if (LooksLikeSecret(text))
        {
            return new HarnessVerifyResult(false, string.Empty, "secret-leak");
        }

        // Soft domain hint: keep response even if model drifted; annotate for observability only.
        _ = intent;
        return new HarnessVerifyResult(true, text, null);
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.Ordinal));

    private static bool LooksLikeSecret(string text) =>
        text.Contains("CURSOR_API_KEY", StringComparison.OrdinalIgnoreCase)
        || text.Contains("api_key=", StringComparison.OrdinalIgnoreCase)
        || text.Contains("apikey=", StringComparison.OrdinalIgnoreCase)
        || (text.Contains("sk-", StringComparison.OrdinalIgnoreCase) && text.Length > 20);
}
