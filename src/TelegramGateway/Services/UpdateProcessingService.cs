using System.Text;
using Microsoft.Extensions.Options;
using TelegramGateway.Contracts;
using TelegramGateway.Options;
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
    private readonly IOptions<TelegramOptions> _telegramOptions;
    private readonly IOptions<ResearchImageOptions> _researchImageOptions;
    private readonly ILogger<UpdateProcessingService> _logger;

    public UpdateProcessingService(
        IAssistantApiClient assistantApiClient,
        ITelegramBotClient telegramBotClient,
        IOptions<TelegramOptions> telegramOptions,
        IOptions<ResearchImageOptions> researchImageOptions,
        ILogger<UpdateProcessingService> logger)
    {
        _assistantApiClient = assistantApiClient;
        _telegramBotClient = telegramBotClient;
        _telegramOptions = telegramOptions;
        _researchImageOptions = researchImageOptions;
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
        var studioKeyboard = TelegramWebAppKeyboard.TryCreate(_telegramOptions.Value.WebAppUrl);

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
                "Telegram AI.\nКоманды: /salon /marketing /tasks\n/research — Instagram research (on|off|account|now|plan)\nКнопка «Открыть студию» → Mini App (Маркетинг).",
                cancellationToken,
                studioKeyboard);
            return;
        }

        if (text.StartsWith("/research", StringComparison.OrdinalIgnoreCase))
        {
            await HandleResearchAsync(chatId, userId, text, studioKeyboard, cancellationToken);
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
        object? studioKeyboard,
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
                await _telegramBotClient.SendMessageAsync(
                    chatId,
                    FormatStatus(latest),
                    cancellationToken,
                    studioKeyboard);
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
                        cancellationToken,
                        studioKeyboard);
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
                        cancellationToken,
                        studioKeyboard);
                    return;
                }
                case "account":
                {
                    if (args.Length < 2)
                    {
                        await _telegramBotClient.SendMessageAsync(
                            chatId,
                            "Использование: /research account @handle",
                            cancellationToken,
                            studioKeyboard);
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
                        cancellationToken,
                        studioKeyboard);
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

                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        sb.ToString().Trim(),
                        cancellationToken,
                        studioKeyboard);
                    return;
                }
                case "plan":
                {
                    var latest = await _assistantApiClient.GetResearchLatestAsync(userId, cancellationToken);
                    var planText = FormatPlanSummary(latest);
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        planText,
                        cancellationToken,
                        studioKeyboard);
                    await SendPlanPhotosAsync(chatId, latest, cancellationToken);
                    return;
                }
                default:
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Команды: /research | on | off | account @handle | now | plan\nСтудия — кнопка Mini App.",
                        cancellationToken,
                        studioKeyboard);
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

    private async Task SendPlanPhotosAsync(
        long chatId,
        ResearchLatestResponse latest,
        CancellationToken cancellationToken)
    {
        var volume = _researchImageOptions.Value.ImageVolumePath;
        if (string.IsNullOrWhiteSpace(volume) || latest.Items.Count == 0)
        {
            return;
        }

        var maxBytes = Math.Clamp(_researchImageOptions.Value.MaxImageBytes, 1024, 20 * 1024 * 1024);
        var sent = 0;
        foreach (var item in latest.Items)
        {
            if (sent >= 3)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(item.MediaPath))
            {
                continue;
            }

            if (!ResearchPhotoPathGuard.TryResolve(volume, item.MediaPath, maxBytes, out var abs, out _))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(abs);
                await _telegramBotClient.SendPhotoAsync(
                    chatId,
                    stream,
                    Path.GetFileName(abs),
                    caption: item.Date.ToString("yyyy-MM-dd"),
                    cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Research plan sendPhoto soft-fail");
            }
        }
    }

    private static string FormatPlanSummary(ResearchLatestResponse latest)
    {
        if (latest.Items.Count == 0 && string.IsNullOrWhiteSpace(latest.PlanPreview))
        {
            return "Плана пока нет. Запусти /research now или дождись scheduler.\nОткрой студию в Mini App для галереи.";
        }

        var sb = new StringBuilder();
        if (latest.PlanWindowStart is { } start && latest.PlanWindowEnd is { } end)
        {
            sb.AppendLine($"План {start:yyyy-MM-dd}…{end:yyyy-MM-dd} ({latest.PlanItemCount ?? latest.Items.Count} дн.)");
        }
        else if (!string.IsNullOrWhiteSpace(latest.PlanPreview))
        {
            // Keep preview short for bot — first ~3 lines.
            var lines = latest.PlanPreview!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines.Take(4))
            {
                sb.AppendLine(line);
            }
        }

        foreach (var item in latest.Items.Take(3))
        {
            var cap = item.Caption.Length <= 60 ? item.Caption : item.Caption[..60] + "…";
            sb.AppendLine($"• {item.Date:MM-dd}: {cap} [{item.Status}]");
        }

        if (latest.Items.Count > 3)
        {
            sb.AppendLine($"…ещё {latest.Items.Count - 3} — в студии Mini App");
        }

        return sb.ToString().Trim();
    }

    private static string[] SplitArgs(string text)
    {
        // "/research@bot on" or "/research on"
        var space = text.IndexOf(' ');
        if (space < 0)
        {
            return [];
        }

        var withoutCmd = text[(space + 1)..].Trim();
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
