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

app.Run();

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

public partial class Program;
