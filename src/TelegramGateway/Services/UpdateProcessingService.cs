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
                case "vk":
                {
                    await HandleResearchVkAsync(
                        chatId, userId, notifyChatId, args.Skip(1).ToArray(), studioKeyboard, cancellationToken);
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
                        "Команды: /research | on | off | account @handle | vk add|remove|list|now | now | plan\nСтудия — кнопка Mini App.",
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

    private async Task HandleResearchVkAsync(
        long chatId,
        string userId,
        string notifyChatId,
        string[] vkArgs,
        object? studioKeyboard,
        CancellationToken cancellationToken)
    {
        var sub = vkArgs.Length == 0 ? "list" : vkArgs[0].ToLowerInvariant();
        switch (sub)
        {
            case "list":
            case "status":
            {
                var settings = await _assistantApiClient.GetResearchSettingsAsync(userId, cancellationToken);
                await _telegramBotClient.SendMessageAsync(
                    chatId,
                    FormatVkAllowlist(settings),
                    cancellationToken,
                    studioKeyboard);
                return;
            }
            case "add":
            {
                if (vkArgs.Length < 2)
                {
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Использование: /research vk add <screen_name|owner_id|club123>",
                        cancellationToken,
                        studioKeyboard);
                    return;
                }

                var raw = string.Join(' ', vkArgs.Skip(1));
                if (SecretScanner.ContainsForbiddenSecret(raw))
                {
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Не принимаю VK token. Только screen_name или owner_id.",
                        cancellationToken);
                    return;
                }

                var target = ParseVkTarget(raw);
                if (target is null)
                {
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Не распознал паблик. Пример: babor_bryansk или -123456 или club123.",
                        cancellationToken,
                        studioKeyboard);
                    return;
                }

                var current = await _assistantApiClient.GetResearchSettingsAsync(userId, cancellationToken);
                var list = current.VkCommunities?.ToList() ?? [];
                if (list.Any(c => SameVk(c, target)))
                {
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        FormatVkAllowlist(current, "Уже в allowlist."),
                        cancellationToken,
                        studioKeyboard);
                    return;
                }

                if (list.Count >= 10)
                {
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Allowlist полный (max 10). Удали: /research vk remove …",
                        cancellationToken,
                        studioKeyboard);
                    return;
                }

                list.Add(target);
                var settings = await _assistantApiClient.PutResearchSettingsAsync(new ResearchSettingsUpdateRequest
                {
                    UserId = userId,
                    VkCommunities = list,
                    NotifyChatId = notifyChatId
                }, cancellationToken);
                await _telegramBotClient.SendMessageAsync(
                    chatId,
                    FormatVkAllowlist(settings, "Добавлено в VK allowlist."),
                    cancellationToken,
                    studioKeyboard);
                return;
            }
            case "remove":
            case "rm":
            case "del":
            {
                if (vkArgs.Length < 2)
                {
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Использование: /research vk remove <screen_name|owner_id>",
                        cancellationToken,
                        studioKeyboard);
                    return;
                }

                var raw = string.Join(' ', vkArgs.Skip(1));
                if (SecretScanner.ContainsForbiddenSecret(raw))
                {
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Не принимаю VK token.",
                        cancellationToken);
                    return;
                }

                var target = ParseVkTarget(raw);
                if (target is null)
                {
                    await _telegramBotClient.SendMessageAsync(
                        chatId,
                        "Не распознал паблик для удаления.",
                        cancellationToken,
                        studioKeyboard);
                    return;
                }

                var current = await _assistantApiClient.GetResearchSettingsAsync(userId, cancellationToken);
                var list = (current.VkCommunities ?? [])
                    .Where(c => !SameVk(c, target))
                    .ToList();
                var settings = await _assistantApiClient.PutResearchSettingsAsync(new ResearchSettingsUpdateRequest
                {
                    UserId = userId,
                    VkCommunities = list,
                    NotifyChatId = notifyChatId
                }, cancellationToken);
                await _telegramBotClient.SendMessageAsync(
                    chatId,
                    FormatVkAllowlist(settings, "Обновлён VK allowlist."),
                    cancellationToken,
                    studioKeyboard);
                return;
            }
            case "now":
            {
                var run = await _assistantApiClient.RunResearchAsync(new ResearchRunRequest
                {
                    UserId = userId,
                    NotifyChatId = notifyChatId,
                    Source = "vk"
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
                    sb.AppendLine(FormatVkAllowlist(run.Settings));
                }

                await _telegramBotClient.SendMessageAsync(
                    chatId,
                    sb.ToString().Trim(),
                    cancellationToken,
                    studioKeyboard);
                return;
            }
            default:
                await _telegramBotClient.SendMessageAsync(
                    chatId,
                    "VK: /research vk list | add <name|id> | remove <name|id> | now\nToken только в env (VK__SERVICETOKEN).",
                    cancellationToken,
                    studioKeyboard);
                return;
        }
    }

    private static VkCommunityTargetDto? ParseVkTarget(string raw)
    {
        var text = raw.Trim()
            .Replace("https://vk.com/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://vk.com/", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimStart('@')
            .Trim('/');
        if (text.Contains('?', StringComparison.Ordinal))
        {
            text = text.Split('?', 2)[0];
        }

        if (text.StartsWith("club", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("public", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("event", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).ToArray());
            if (long.TryParse(digits, out var clubId) && clubId > 0)
            {
                return new VkCommunityTargetDto { OwnerId = -clubId };
            }
        }

        if (long.TryParse(text, out var ownerId) && ownerId != 0)
        {
            return new VkCommunityTargetDto { OwnerId = ownerId };
        }

        if (text.Length is >= 2 and <= 64 && text.All(c => char.IsLetterOrDigit(c) || c is '.' or '_'))
        {
            return new VkCommunityTargetDto { ScreenName = text };
        }

        return null;
    }

    private static bool SameVk(VkCommunityTargetDto a, VkCommunityTargetDto b)
    {
        if (a.OwnerId is long ao && b.OwnerId is long bo && ao == bo)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(a.ScreenName)
               && !string.IsNullOrWhiteSpace(b.ScreenName)
               && string.Equals(a.ScreenName, b.ScreenName, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatVkAllowlist(ResearchSettingsDto s, string? head = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(head))
        {
            sb.AppendLine(head);
        }

        var list = s.VkCommunities ?? [];
        sb.AppendLine($"VK allowlist ({list.Count}/10):");
        if (list.Count == 0)
        {
            sb.AppendLine("— пусто. /research vk add <screen_name>");
        }
        else
        {
            foreach (var c in list)
            {
                if (!string.IsNullOrWhiteSpace(c.ScreenName) && c.OwnerId is long oid)
                {
                    sb.AppendLine($"• {c.ScreenName} ({oid})");
                }
                else if (!string.IsNullOrWhiteSpace(c.ScreenName))
                {
                    sb.AppendLine($"• {c.ScreenName}");
                }
                else
                {
                    sb.AppendLine($"• owner={c.OwnerId}");
                }
            }
        }

        return sb.ToString().Trim();
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
        var vkCount = s.VkCommunities?.Count ?? 0;
        sb.AppendLine($"vk: {vkCount} public(s)");
        sb.AppendLine($"cadence: {s.CadenceDays}d tz={s.Timezone ?? "—"}");
        sb.AppendLine($"last: {Fmt(s.LastRunAt)} next: {Fmt(s.NextRunAt)}");
        if (!string.IsNullOrWhiteSpace(s.LastError))
        {
            sb.AppendLine($"lastError: {s.LastError}");
        }

        if (vkCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine(FormatVkAllowlist(s));
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
        sb.AppendLine($"vk={(s.VkCommunities?.Count ?? 0)}");
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
