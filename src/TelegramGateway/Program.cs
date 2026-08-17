using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TelegramGateway.Contracts;
using TelegramGateway.Options;
using TelegramGateway.Security;
using TelegramGateway.Services;

var builder = WebApplication.CreateBuilder(args);

// Never log Telegram Bot HTTP URIs — token is in the path.
builder.Logging.AddFilter("System.Net.Http.HttpClient.ITelegramBotClient", LogLevel.None);

builder.Services
    .AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection(TelegramOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<AssistantClientOptions>()
    .Bind(builder.Configuration.GetSection(AssistantClientOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IUpdateProcessingService, UpdateProcessingService>();
builder.Services.AddHttpClient<ITelegramBotClient, TelegramBotClient>();
builder.Services.AddHttpClient<IAssistantApiClient, AssistantApiClient>();
builder.Services.AddHostedService<TelegramPollingHostedService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapPost("/telegram/webhook", async (
    HttpRequest httpRequest,
    [FromBody] TelegramUpdate update,
    IUpdateProcessingService processor,
    IOptions<TelegramOptions> telegramOptions,
    CancellationToken cancellationToken) =>
{
    var expected = telegramOptions.Value.WebhookSecretToken;
    if (!string.IsNullOrWhiteSpace(expected))
    {
        if (!httpRequest.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var provided) ||
            !string.Equals(provided.ToString(), expected, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }
    }

    await processor.ProcessAsync(update, cancellationToken);
    return Results.Ok();
});

// Mini App -> gateway proxy to assistant-api (service key stays server-side).
app.MapPost("/api/miniapp/chat", async (
    [FromBody] MiniAppChatRequest request,
    IAssistantApiClient assistantApiClient,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.Problem("text is required", statusCode: StatusCodes.Status400BadRequest);
    }

    if (SecretScanner.ContainsForbiddenSecret(request.Text))
    {
        return Results.Problem(
            "Secrets and API keys must not be sent from Mini App.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Secret rejected");
    }

    var intent = NormalizeIntent(request.Intent);
    var chatRequest = new AssistantChatRequest
    {
        SchemaVersion = 1,
        ConversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? $"mini-{Guid.NewGuid():N}"
            : request.ConversationId!,
        UserId = string.IsNullOrWhiteSpace(request.UserId) ? "mini-anonymous" : request.UserId!,
        Text = request.Text.Trim(),
        TraceId = Guid.NewGuid().ToString("N"),
        Intent = intent
    };

    var response = await assistantApiClient.ChatAsync(chatRequest, cancellationToken);
    return Results.Ok(response);
});

// Phase 4 research Mini App proxies — mutations require tg-* userId (no anonymous writes).
app.MapGet("/api/miniapp/research/settings", async (
    [FromQuery] string? userId,
    IAssistantApiClient assistant,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(userId))
    {
        return Results.Problem(
            "userId must be tg-<id> (open Mini App from Telegram).",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    var dto = await assistant.GetResearchSettingsAsync(userId!, cancellationToken);
    return Results.Ok(dto);
});

app.MapPut("/api/miniapp/research/settings", async (
    [FromBody] MiniAppResearchSettingsRequest request,
    IAssistantApiClient assistant,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(request.UserId))
    {
        return Results.Problem(
            "userId must be tg-<id>. Anonymous writes rejected.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    if (SecretScanner.ContainsForbiddenSecret(request.InstagramHandle) ||
        SecretScanner.ContainsForbiddenSecret(request.Timezone) ||
        SecretScanner.ContainsForbiddenSecret(request.NotifyChatId))
    {
        return Results.Problem(
            "IG token / secrets must not be sent from Mini App.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Secret rejected");
    }

    try
    {
        var dto = await assistant.PutResearchSettingsAsync(new ResearchSettingsUpdateRequest
        {
            UserId = request.UserId!,
            Enabled = request.Enabled,
            InstagramHandle = request.InstagramHandle,
            CadenceDays = request.CadenceDays ?? 14,
            Timezone = request.Timezone,
            NotifyChatId = request.NotifyChatId
        }, cancellationToken);
        return Results.Ok(dto);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: (int?)ex.StatusCode ?? StatusCodes.Status502BadGateway,
            title: "assistant-api error");
    }
});

app.MapPost("/api/miniapp/research/run", async (
    [FromBody] MiniAppResearchRunRequest request,
    IAssistantApiClient assistant,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(request.UserId))
    {
        return Results.Problem(
            "userId must be tg-<id>. Anonymous runs rejected.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    try
    {
        var result = await assistant.RunResearchAsync(new ResearchRunRequest
        {
            UserId = request.UserId!,
            NotifyChatId = request.NotifyChatId
        }, cancellationToken);
        return Results.Ok(result);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: (int?)ex.StatusCode ?? StatusCodes.Status502BadGateway,
            title: "assistant-api error");
    }
});

app.MapGet("/api/miniapp/research/latest", async (
    [FromQuery] string? userId,
    IAssistantApiClient assistant,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(userId))
    {
        return Results.Problem(
            "userId must be tg-<id>.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    var latest = await assistant.GetResearchLatestAsync(userId!, cancellationToken);
    return Results.Ok(latest);
});

// Internal: assistant-api research notify → Telegram sendMessage (service key required).
app.MapPost("/internal/notify", async (
    HttpRequest httpRequest,
    [FromBody] InternalNotifyRequest request,
    ITelegramBotClient telegram,
    IOptions<AssistantClientOptions> assistantOptions,
    CancellationToken cancellationToken) =>
{
    if (!ServiceKeyMatches(httpRequest, assistantOptions.Value.ServiceKey))
    {
        return Results.Unauthorized();
    }

    if (request.ChatId == 0 || string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.Problem("chatId and text are required", statusCode: StatusCodes.Status400BadRequest);
    }

    if (SecretScanner.ContainsForbiddenSecret(request.Text))
    {
        return Results.Problem(
            "Notify text rejected: looks like a secret.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Secret rejected");
    }

    var text = request.Text.Length <= 3500 ? request.Text : request.Text[..3500] + "…";
    await telegram.SendMessageAsync(request.ChatId, text, cancellationToken);
    return Results.Ok(new { ok = true });
});

app.Run();

static bool ServiceKeyMatches(HttpRequest request, string expected)
{
    if (string.IsNullOrWhiteSpace(expected))
    {
        return false;
    }

    if (!request.Headers.TryGetValue("X-Service-Key", out var provided) ||
        string.IsNullOrWhiteSpace(provided))
    {
        return false;
    }

    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
    return expectedBytes.Length == providedBytes.Length
           && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
}

static bool IsTelegramUserId(string? userId) =>
    !string.IsNullOrWhiteSpace(userId)
    && userId.StartsWith("tg-", StringComparison.Ordinal)
    && userId.Length > 3
    && userId.Length <= 64
    && userId[3..].All(char.IsDigit);

static string? NormalizeIntent(string? intent)
{
    if (string.IsNullOrWhiteSpace(intent))
    {
        return null;
    }

    return intent.Trim().ToLowerInvariant() switch
    {
        "salon" => "salon",
        "marketing" => "marketing",
        "tasks" => "tasks",
        "general" => "general",
        _ => null
    };
}

public sealed class MiniAppChatRequest
{
    public string? ConversationId { get; set; }
    public string? UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Intent { get; set; }
}

public sealed class MiniAppResearchSettingsRequest
{
    public string? UserId { get; set; }
    public bool? Enabled { get; set; }
    public string? InstagramHandle { get; set; }
    public int? CadenceDays { get; set; }
    public string? Timezone { get; set; }
    public string? NotifyChatId { get; set; }
}

public sealed class MiniAppResearchRunRequest
{
    public string? UserId { get; set; }
    public string? NotifyChatId { get; set; }
}

public partial class Program;
