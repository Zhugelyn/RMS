using System.Text;
using TelegramGateway.Contracts;
using TelegramGateway.Security;

namespace TelegramGateway.Services;

public interface IUpdateProcessingService
{
    Task ProcessAsync(TelegramUpdate update, CancellationToken cancellationToken);
}

public sealed class UpdateProcessingService : IUpdateProcessingService
{
    private readonly IAssistantApiClient _assistantApiClient;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly ILogger<UpdateProcessingService> _logger;

    public UpdateProcessingService(
        IAssistantApiClient assistantApiClient,
        ITelegramBotClient telegramBotClient,
        ILogger<UpdateProcessingService> logger)
    {
        _assistantApiClient = assistantApiClient;
        _telegramBotClient = telegramBotClient;
        _logger = logger;
    }

    public async Task ProcessAsync(TelegramUpdate update, CancellationToken cancellationToken)
    {
        var message = update.Message;
        if (message?.Chat is null || string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        var chatId = message.Chat.Id;
        var userId = message.From?.Id.ToString() ?? chatId.ToString();
        var text = message.Text.Trim();
        var traceId = Guid.NewGuid().ToString("N");

        if (SecretScanner.ContainsForbiddenSecret(text))
        {
            _logger.LogWarning("Rejected message with suspected secret userId={UserId} traceId={TraceId}", userId, traceId);
            await _telegramBotClient.SendMessageAsync(
                chatId,
                "Не принимаю API keys / секреты из чата. Настрой ключи через secret store сервиса.",
                cancellationToken);
            return;
        }

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await _telegramBotClient.SendMessageAsync(
                chatId,
                "Telegram AI.\nКоманды: /salon /marketing /tasks\n/research — Instagram research (on|off|account|now|plan)\nИли открой Mini App → Маркетинг.",
                cancellationToken);
            return;
        }

        if (text.StartsWith("/research", StringComparison.OrdinalIgnoreCase))
        {
            await HandleResearchAsync(chatId, userId, text, cancellationToken);
            return;
        }

        var intent = ResolveIntent(text);
        var request = new AssistantChatRequest
        {
            SchemaVersion = 1,
            ConversationId = $"tg-{chatId}",
            UserId = $"tg-{userId}",
            Text = StripCommand(text),
            TraceId = traceId,
            Intent = intent
        };

        try
        {
            var response = await _assistantApiClient.ChatAsync(request, cancellationToken);
            await _telegramBotClient.SendMessageAsync(chatId, response.Text, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to process update traceId={TraceId}", traceId);
            try
            {
                await _telegramBotClient.SendMessageAsync(
                    chatId,
                    "Временная ошибка ассистента. Попробуй ещё раз.",
                    cancellationToken);
            }
            catch (Exception sendEx) when (sendEx is not OperationCanceledException)
            {
                _logger.LogWarning(sendEx, "Failed to send error reply traceId={TraceId}", traceId);
            }
        }
    }

    private async Task HandleResearchAsync(
        long chatId,
        string telegramUserId,
        string text,
        CancellationToken cancellationToken)
    {
        var userId = $"tg-{telegramUserId}";
        var notifyChatId = chatId.ToString();
        var args = SplitArgs(text);

        try
        {
            if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                var latest = await _assistantApiClient.GetResearchLatestAsync(userId, cancellationToken);
                await _telegramBotClient.SendMessageAsync(chatId, FormatStatus(latest), cancellationToken);
                return;
            }

            var cmd = args[0].ToLowerInvariant();
            switch (cmd)
            {
                case "on":
                {
                    var settings = await _assistantApiClient.PutResearchSettingsAsync(new ResearchSettingsUpdateRequest
                    {
                        UserId = userId,
                        Enabled = true,
                        NotifyChatId = notifyChatId
                    }, cancellationToken);
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        FormatSettingsLine(settings, "Research включён. Уведомления → этот чат."),
                        cancellationToken);
                    return;
                }
                case "off":
                {
                    var settings = await _assistantApiClient.PutResearchSettingsAsync(new ResearchSettingsUpdateRequest
                    {
                        UserId = userId,
                        Enabled = false,
                        NotifyChatId = notifyChatId
                    }, cancellationToken);
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        FormatSettingsLine(settings, "Research выключен."),
                        cancellationToken);
                    return;
                }
                case "account":
                {
                    if (args.Length < 2)
                    {
                        await _telegramBotClient.SendMessageAsync(
                            chatId,
                            "Использование: /research account @handle",
                            cancellationToken);
                        return;
                    }

                    var handle = string.Join(' ', args.Skip(1));
                    if (SecretScanner.ContainsForbiddenSecret(handle))
                    {
                        await _telegramBotClient.SendMessageAsync(
                            chatId,
                            "Не принимаю токены. Передай только @handle аккаунта.",
                            cancellationToken);
                        return;
                    }

                    var settings = await _assistantApiClient.PutResearchSettingsAsync(new ResearchSettingsUpdateRequest
                    {
                        UserId = userId,
                        InstagramHandle = handle,
                        NotifyChatId = notifyChatId
                    }, cancellationToken);
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        FormatSettingsLine(settings, $"Аккаунт: @{settings.InstagramHandle}"),
                        cancellationToken);
                    return;
                }
                case "now":
                {
                    var run = await _assistantApiClient.RunResearchAsync(new ResearchRunRequest
                    {
                        UserId = userId,
                        NotifyChatId = notifyChatId
                    }, cancellationToken);
                    var sb = new StringBuilder();
                    sb.AppendLine(run.Message ?? run.Outcome);
                    if (!string.IsNullOrWhiteSpace(run.ErrorCode))
                    {
                        sb.AppendLine($"code={run.ErrorCode}");
                    }

                    if (!string.IsNullOrWhiteSpace(run.PlanPreview))
                    {
                        sb.AppendLine();
                        sb.AppendLine(run.PlanPreview);
                    }
                    else
                    {
                        sb.AppendLine(FormatSettingsLine(run.Settings, null));
                    }

                    await _telegramBotClient.SendMessageAsync(chatId, sb.ToString().Trim(), cancellationToken);
                    return;
                }
                case "plan":
                {
                    var latest = await _assistantApiClient.GetResearchLatestAsync(userId, cancellationToken);
                    var planText = string.IsNullOrWhiteSpace(latest.PlanPreview)
                        ? "Плана пока нет. Запусти /research now или дождись scheduler."
                        : latest.PlanPreview!;
                    await _telegramBotClient.SendMessageAsync(chatId, planText, cancellationToken);
                    return;
                }
                default:
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Команды: /research | on | off | account @handle | now | plan",
                        cancellationToken);
                    return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Research command failed userId={UserId}", userId);
            await _telegramBotClient.SendMessageAsync(
                chatId,
                "Ошибка research. Попробуй ещё раз.",
                cancellationToken);
        }
    }

    private static string[] SplitArgs(string text)
    {
        // "/research@bot on" or "/research on"
        var withoutCmd = text;
        var space = text.IndexOf(' ');
        if (space < 0)
        {
            return [];
        }

        withoutCmd = text[(space + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(withoutCmd))
        {
            return [];
        }

        return withoutCmd.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string FormatStatus(ResearchLatestResponse latest)
    {
        var s = latest.Settings;
        var sb = new StringBuilder();
        sb.AppendLine($"Research: {(s.Enabled ? "on" : "off")}");
        sb.AppendLine($"account: {(string.IsNullOrWhiteSpace(s.InstagramHandle) ? "—" : "@" + s.InstagramHandle)}");
        sb.AppendLine($"cadence: {s.CadenceDays}d tz={s.Timezone ?? "—"}");
        sb.AppendLine($"last: {Fmt(s.LastRunAt)} next: {Fmt(s.NextRunAt)}");
        if (!string.IsNullOrWhiteSpace(s.LastError))
        {
            sb.AppendLine($"lastError: {s.LastError}");
        }

        if (!string.IsNullOrWhiteSpace(latest.SnapshotSummary))
        {
            sb.AppendLine();
            sb.AppendLine("Snapshot: " + latest.SnapshotSummary);
        }

        if (!string.IsNullOrWhiteSpace(latest.PlanPreview))
        {
            sb.AppendLine();
            sb.AppendLine(latest.PlanPreview);
        }

        return sb.ToString().Trim();
    }

    private static string FormatSettingsLine(ResearchSettingsDto s, string? head)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(head))
        {
            sb.AppendLine(head);
        }

        sb.AppendLine($"enabled={s.Enabled} cadence={s.CadenceDays}d");
        sb.AppendLine($"account={(string.IsNullOrWhiteSpace(s.InstagramHandle) ? "—" : "@" + s.InstagramHandle)}");
        sb.AppendLine($"last={Fmt(s.LastRunAt)} next={Fmt(s.NextRunAt)}");
        if (!string.IsNullOrWhiteSpace(s.LastError))
        {
            sb.AppendLine($"lastError={s.LastError}");
        }

        return sb.ToString().Trim();
    }

    private static string Fmt(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + "Z";

    private static string? ResolveIntent(string text)
    {
        if (text.StartsWith("/salon", StringComparison.OrdinalIgnoreCase))
        {
            return "salon";
        }

        if (text.StartsWith("/marketing", StringComparison.OrdinalIgnoreCase))
        {
            return "marketing";
        }

        if (text.StartsWith("/tasks", StringComparison.OrdinalIgnoreCase))
        {
            return "tasks";
        }

        return null;
    }

    private static string StripCommand(string text)
    {
        if (!text.StartsWith('/'))
        {
            return text;
        }

        var space = text.IndexOf(' ');
        if (space < 0)
        {
            return "Краткий статус по выбранному домену.";
        }

        var rest = text[(space + 1)..].Trim();
        return string.IsNullOrWhiteSpace(rest)
            ? "Краткий статус по выбранному домену."
            : rest;
    }
}
