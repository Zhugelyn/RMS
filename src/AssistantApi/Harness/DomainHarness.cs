using AssistantApi.Contracts;
using AssistantApi.Packs;

namespace AssistantApi.Harness;

/// <summary>Phase 3 classify helpers + hard verify. Router SDK path lives in CursorSdkLlmProvider.</summary>
public sealed class DomainHarness : IDomainHarness
{
    private readonly IPackCatalog? _packs;

    public DomainHarness()
    {
    }

    public DomainHarness(IPackCatalog packs)
    {
        _packs = packs;
    }

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

        if (request.Intent is ChatIntent.General)
        {
            return DomainIntent.General;
        }

        return ClassifyFromPackHints(request.Text);
    }

    public string BuildSpecialistPrompt(DomainIntent intent, ChatRequest request)
    {
        // Legacy Phase 2 path kept for unit tests; Cursor provider uses PackPromptBuilder.
        var domain = intent switch
        {
            DomainIntent.Salon => "салон Babor (Брянск): развивай салон, услуги, локальный маркетинг ради салона",
            DomainIntent.Marketing => "маркетинг: рынок красоты, бренды, аудитории, таргет",
            DomainIntent.Tasks => "задачи: расписание работ и напоминания",
            _ => "general: уточни домен salon/marketing/tasks"
        };

        return
            $"Ты specialist Telegram AI. Короткий ответ на русском, без секретов.\nДомен: {domain}\n" +
            $"conversationId={request.ConversationId}\nuserId={request.UserId}\ntraceId={request.TraceId}\n" +
            $"Запрос:\n{request.Text.Trim()}";
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

        if (LooksLikeDomainDrift(intent, text))
        {
            return new HarnessVerifyResult(false, string.Empty, "domain-drift");
        }

        return new HarnessVerifyResult(true, text, null);
    }

    public static string ToPackId(DomainIntent intent) => intent switch
    {
        DomainIntent.Salon => PackIds.Salon,
        DomainIntent.Marketing => PackIds.Marketing,
        DomainIntent.Tasks => PackIds.Tasks,
        // Product default when router returns general: Babor salon growth assistant.
        _ => PackIds.Salon
    };

    public static DomainIntent FromPackId(string packId) => packId switch
    {
        PackIds.Salon => DomainIntent.Salon,
        PackIds.Marketing => DomainIntent.Marketing,
        PackIds.Tasks => DomainIntent.Tasks,
        _ => DomainIntent.General
    };

    public static DomainIntent ParseRouterLabel(string raw)
    {
        var token = raw.Trim().ToLowerInvariant();
        foreach (var line in token.Split(['\r', '\n', ' ', ',', '.', ':', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim().Trim('`', '"', '\'');
            if (t is "salon" or "babor")
            {
                return DomainIntent.Salon;
            }

            if (t is "marketing" or "ads" or "market")
            {
                return DomainIntent.Marketing;
            }

            if (t is "tasks" or "task" or "schedule")
            {
                return DomainIntent.Tasks;
            }

            if (t is "general")
            {
                return DomainIntent.General;
            }
        }

        if (token.Contains("salon", StringComparison.Ordinal) || token.Contains("babor", StringComparison.Ordinal))
        {
            return DomainIntent.Salon;
        }

        if (token.Contains("marketing", StringComparison.Ordinal))
        {
            return DomainIntent.Marketing;
        }

        if (token.Contains("task", StringComparison.Ordinal))
        {
            return DomainIntent.Tasks;
        }

        return DomainIntent.General;
    }

    private DomainIntent ClassifyFromPackHints(string text)
    {
        var lower = text.ToLowerInvariant();
        // Score using pack classify-hints when catalog available; else compact fallback.
        if (_packs is not null)
        {
            var scores = new Dictionary<DomainIntent, int>
            {
                [DomainIntent.Salon] = Score(lower, _packs, PackIds.Salon),
                [DomainIntent.Marketing] = Score(lower, _packs, PackIds.Marketing),
                [DomainIntent.Tasks] = Score(lower, _packs, PackIds.Tasks)
            };

            var best = scores.OrderByDescending(kv => kv.Value).First();
            if (best.Value > 0 && scores.Count(kv => kv.Value == best.Value) == 1)
            {
                return best.Key;
            }

            if (best.Value > 0)
            {
                // Tie-break preference: explicit Babor/salon ops > schedule > market
                if (scores[DomainIntent.Salon] == best.Value)
                {
                    return DomainIntent.Salon;
                }

                if (scores[DomainIntent.Tasks] == best.Value)
                {
                    return DomainIntent.Tasks;
                }

                return DomainIntent.Marketing;
            }
        }

        if (ContainsAny(lower, "babor", "бабор", "брянск", "салон", "запись", "мастер", "стрижк", "маникюр", "услуг"))
        {
            return DomainIntent.Salon;
        }

        if (ContainsAny(lower, "рынок", "бренд", "космети", "таргет", "аудитор", "реклам", "кампан", "тренд"))
        {
            return DomainIntent.Marketing;
        }

        if (ContainsAny(lower, "расписан", "напомин", "встреч", "todo", "дедлайн", "слот", "смен"))
        {
            return DomainIntent.Tasks;
        }

        return DomainIntent.General;
    }

    private static int Score(string lower, IPackCatalog packs, string packId)
    {
        if (!packs.TryGet(packId, out var pack))
        {
            return 0;
        }

        var hintsPath = Path.Combine(pack.PromptsDirectoryPath, "classify-hints.md");
        if (!File.Exists(hintsPath))
        {
            return 0;
        }

        var hints = File.ReadAllText(hintsPath).ToLowerInvariant();
        var score = 0;
        foreach (var token in hints.Split([' ', ',', ';', ':', '\n', '\r', '/', '|', '.', '—', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim();
            if (t.Length < 4)
            {
                continue;
            }

            if (lower.Contains(t, StringComparison.Ordinal))
            {
                score++;
            }
        }

        return score;
    }

    private static bool LooksLikeDomainDrift(DomainIntent intent, string text)
    {
        var lower = text.ToLowerInvariant();
        return intent switch
        {
            DomainIntent.Salon =>
                !ContainsAny(lower, "babor", "бабор", "салон", "брянск", "мастер", "услуг", "клиент", "запис", "удержан", "локальн")
                && ContainsAny(lower, "доля рынка", "топ бренд", "таргет аудитории", "рынок красоты в целом")
                && !ContainsAny(lower, "для babor", "для салона", "вашего салона"),
            DomainIntent.Marketing =>
                ContainsAny(lower, "запишу вас к мастеру", "подтверждаю бронь", "слот на маникюр")
                && !ContainsAny(lower, "аудитор", "кампан", "таргет", "бренд", "рынок"),
            DomainIntent.Tasks =>
                ContainsAny(lower, "стратегия развития babor", "анализ рынка косметики", "топ брендов")
                && !ContainsAny(lower, "расписан", "напомин", "слот", "дедлайн", "встреч", "план"),
            _ => false
        };
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.Ordinal));

    private static bool LooksLikeSecret(string text) =>
        text.Contains("CURSOR_API_KEY", StringComparison.OrdinalIgnoreCase)
        || text.Contains("INSTAGRAM__ACCESSTOKEN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("INSTAGRAM_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("IG_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("VK__SERVICETOKEN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("VK_SERVICE_TOKEN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("VK_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("api_key=", StringComparison.OrdinalIgnoreCase)
        || text.Contains("apikey=", StringComparison.OrdinalIgnoreCase)
        || text.Contains("access_token=", StringComparison.OrdinalIgnoreCase)
        || text.Contains("service_token=", StringComparison.OrdinalIgnoreCase)
        || text.Contains("IGQVJ", StringComparison.OrdinalIgnoreCase)
        || text.Contains("IGQWR", StringComparison.OrdinalIgnoreCase)
        || (text.Contains("sk-", StringComparison.OrdinalIgnoreCase) && text.Length > 20);
}
