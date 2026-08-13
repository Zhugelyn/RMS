using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TelegramGateway.Options;

namespace TelegramGateway.Services;

public interface ITelegramBotClient
{
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken);
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken);
}

public sealed class TelegramBotClient : ITelegramBotClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramBotClient> _logger;

    public TelegramBotClient(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramBotClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        // Token only lives in gateway HttpClient base address; never logged.
        var token = options.Value.BotToken;
        _httpClient.BaseAddress = new Uri($"https://api.telegram.org/bot{token}/");
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "sendMessage",
            new { chat_id = chatId, text },
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Telegram sendMessage failed status={StatusCode}", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"getUpdates?timeout=25&offset={offset}",
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TelegramGetUpdatesResponse>(JsonOptions, cancellationToken);
        return payload?.Result ?? [];
    }
}

public sealed class TelegramGetUpdatesResponse
{
    public bool Ok { get; set; }
    public List<TelegramUpdate> Result { get; set; } = [];
}

public sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    public TelegramMessage? Message { get; set; }
}

public sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    public TelegramChat? Chat { get; set; }
    public TelegramUser? From { get; set; }
    public string? Text { get; set; }
}

public sealed class TelegramChat
{
    public long Id { get; set; }
}

public sealed class TelegramUser
{
    public long Id { get; set; }
    public string? Username { get; set; }
}
