using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TelegramGateway.Options;

namespace TelegramGateway.Services;

public interface ITelegramBotClient
{
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken);
    Task SendPhotoAsync(long chatId, Stream photo, string fileName, string? caption, CancellationToken cancellationToken);
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken);
}

public sealed class TelegramBotClient : ITelegramBotClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly ILogger<TelegramBotClient> _logger;

    public TelegramBotClient(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramBotClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _botToken = options.Value.BotToken;
        // Base address without token so HttpClient logging cannot leak it.
        _httpClient.BaseAddress = new Uri("https://api.telegram.org/");
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        // Leading '/' required: token contains ':' and would otherwise be parsed as a URI scheme.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/bot{_botToken}/sendMessage")
        {
            Content = JsonContent.Create(new { chat_id = chatId, text }, options: JsonOptions)
        };

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram sendMessage failed status={StatusCode}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Soft-fail: placeholder/dev tokens must not crash webhook processing.
            _logger.LogWarning(ex, "Telegram sendMessage failed");
        }
    }

    public async Task SendPhotoAsync(
        long chatId,
        Stream photo,
        string fileName,
        string? caption,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(chatId.ToString()), "chat_id");
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var cap = caption.Length <= 1024 ? caption : caption[..1024];
            content.Add(new StringContent(cap), "caption");
        }

        var streamContent = new StreamContent(photo);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(GuessContentType(fileName));
        content.Add(streamContent, "photo", string.IsNullOrWhiteSpace(fileName) ? "image.jpg" : fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/bot{_botToken}/sendPhoto")
        {
            Content = content
        };

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram sendPhoto failed status={StatusCode}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telegram sendPhoto failed");
        }
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/bot{_botToken}/getUpdates?timeout=25&offset={offset}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Telegram getUpdates failed status={StatusCode}", (int)response.StatusCode);
            return [];
        }

        var payload = await response.Content.ReadFromJsonAsync<TelegramGetUpdatesResponse>(JsonOptions, cancellationToken);
        return payload?.Result ?? [];
    }

    private static string GuessContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
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
