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

        var intent = ResolveIntent(text);
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await _telegramBotClient.SendMessageAsync(
                chatId,
                "Telegram AI Phase 1 shell.\nКоманды: /salon /marketing /tasks\nИли открой Mini App.",
                cancellationToken);
            return;
        }

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
            await _telegramBotClient.SendMessageAsync(
                chatId,
                "Временная ошибка ассистента. Попробуй ещё раз.",
                cancellationToken);
        }
    }

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
