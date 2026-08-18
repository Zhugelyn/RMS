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

builder.Services
    .AddOptions<ResearchImageOptions>()
    .Bind(builder.Configuration.GetSection(ResearchImageOptions.SectionName));

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IUpdateProcessingService, UpdateProcessingService>();
builder.Services.AddHttpClient<ITelegramBotClient, TelegramBotClient>();
builder.Services.AddHttpClient<IAssistantApiClient, AssistantApiClient>();
builder.Services.AddHostedService<TelegramPollingHostedService>();
builder.Services.AddHostedService<TelegramWebAppMenuHostedService>();

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
    HttpRequest httpRequest,
    [FromBody] MiniAppResearchSettingsRequest request,
    IAssistantApiClient assistant,
    IOptions<TelegramOptions> telegramOptions,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(request.UserId))
    {
        return Results.Problem(
            "userId must be tg-<id>. Anonymous writes rejected.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    var auth = RequireInitDataUser(httpRequest, request.InitData, request.UserId!, telegramOptions.Value);
    if (auth is not null)
    {
        return auth;
    }

    if (SecretScanner.ContainsForbiddenSecret(request.InstagramHandle) ||
        SecretScanner.ContainsForbiddenSecret(request.Timezone) ||
        SecretScanner.ContainsForbiddenSecret(request.NotifyChatId) ||
        (request.VkCommunities is not null &&
         request.VkCommunities.Any(c => SecretScanner.ContainsForbiddenSecret(c.ScreenName))))
    {
        return Results.Problem(
            "IG/VK token / secrets must not be sent from Mini App.",
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
            VkCommunities = request.VkCommunities,
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
    HttpRequest httpRequest,
    [FromBody] MiniAppResearchRunRequest request,
    IAssistantApiClient assistant,
    IOptions<TelegramOptions> telegramOptions,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(request.UserId))
    {
        return Results.Problem(
            "userId must be tg-<id>. Anonymous runs rejected.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    var auth = RequireInitDataUser(httpRequest, request.InitData, request.UserId!, telegramOptions.Value);
    if (auth is not null)
    {
        return auth;
    }

    try
    {
        var result = await assistant.RunResearchAsync(new ResearchRunRequest
        {
            UserId = request.UserId!,
            NotifyChatId = request.NotifyChatId,
            Source = request.Source
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
    EnrichPlanImageUrls(latest);
    return Results.Ok(latest);
});

// Mini App media proxy — initData HMAC + ResearchPhotoPathGuard; no IG token / no direct volume.
app.MapGet("/api/miniapp/research/media", (
    HttpRequest httpRequest,
    [FromQuery] string? path,
    IOptions<TelegramOptions> telegramOptions,
    IOptions<ResearchImageOptions> researchOptions) =>
{
    var auth = RequireInitData(httpRequest, telegramOptions.Value);
    if (auth is not null)
    {
        return auth;
    }

    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.Problem(
            "path is required",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    // Reject secret-looking query fragments (IG token must never appear in URL).
    if (SecretScanner.ContainsForbiddenSecret(path))
    {
        return Results.Problem(
            "Secrets must not appear in media path.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Secret rejected");
    }

    var volume = researchOptions.Value.ImageVolumePath;
    var maxBytes = Math.Clamp(researchOptions.Value.MaxImageBytes, 1024, 20 * 1024 * 1024);
    if (!ResearchPhotoPathGuard.TryResolve(volume, path, maxBytes, out var abs, out var err))
    {
        var code = err switch
        {
            "path-traversal" or "path-escape" => StatusCodes.Status403Forbidden,
            "not-found" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            detail: $"Media path rejected ({err}).",
            statusCode: code,
            title: "Media denied");
    }

    var contentType = Path.GetExtension(abs).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream"
    };

    var stream = File.OpenRead(abs);
    return Results.File(stream, contentType, enableRangeProcessing: false);
});

// Internal: assistant-api research notify → Telegram sendMessage (+ optional sendPhoto).
app.MapPost("/internal/notify", async (
    HttpRequest httpRequest,
    [FromBody] InternalNotifyRequest request,
    ITelegramBotClient telegram,
    IOptions<AssistantClientOptions> assistantOptions,
    IOptions<ResearchImageOptions> researchOptions,
    ILoggerFactory loggerFactory,
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
    // Always send plan/status text first — fail images ≠ fail notify text.
    await telegram.SendMessageAsync(request.ChatId, text, cancellationToken);

    var volume = !string.IsNullOrWhiteSpace(request.ImageVolumePath)
        ? request.ImageVolumePath!
        : researchOptions.Value.ImageVolumePath;
    var maxBytes = Math.Clamp(researchOptions.Value.MaxImageBytes, 1024, 20 * 1024 * 1024);
    var maxPhotos = Math.Clamp(researchOptions.Value.MaxPhotosPerNotify, 0, 14);
    var log = loggerFactory.CreateLogger("InternalNotify");

    if (request.PhotoPaths is { Count: > 0 } && !string.IsNullOrWhiteSpace(volume) && maxPhotos > 0)
    {
        var sent = 0;
        foreach (var rel in request.PhotoPaths)
        {
            if (sent >= maxPhotos)
            {
                break;
            }

            if (!ResearchPhotoPathGuard.TryResolve(volume, rel, maxBytes, out var abs, out var err))
            {
                log.LogWarning("Research photo skipped reason={Reason}", err);
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(abs);
                await telegram.SendPhotoAsync(
                    request.ChatId,
                    stream,
                    Path.GetFileName(abs),
                    caption: null,
                    cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Research sendPhoto soft-fail");
            }
        }
    }

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

/// <summary>
/// Mini App research mutations require Telegram initData HMAC (not just tg-* prefix).
/// Header <c>X-Telegram-Init-Data</c> preferred; body <c>initData</c> accepted as fallback.
/// </summary>
static IResult? RequireInitDataUser(
    HttpRequest httpRequest,
    string? bodyInitData,
    string userId,
    TelegramOptions options)
{
    string? initData = null;
    if (httpRequest.Headers.TryGetValue("X-Telegram-Init-Data", out var header)
        && !string.IsNullOrWhiteSpace(header))
    {
        initData = header.ToString();
    }
    else if (!string.IsNullOrWhiteSpace(bodyInitData))
    {
        initData = bodyInitData;
    }

    var validated = TelegramInitDataValidator.Validate(
        initData,
        options.BotToken,
        options.InitDataMaxAgeSeconds);
    if (!validated.Ok || validated.UserId is null)
    {
        return Results.Problem(
            detail: $"Telegram initData required for research mutations ({validated.ErrorCode ?? "invalid"}).",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized");
    }

    var expected = $"tg-{validated.UserId.Value}";
    if (!string.Equals(userId, expected, StringComparison.Ordinal))
    {
        return Results.Problem(
            detail: "userId must match Telegram initData user.",
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden");
    }

    return null;
}

/// <summary>Media proxy: valid initData required (header or query <c>initData</c>). No userId match — path guard is the ACL.</summary>
static IResult? RequireInitData(HttpRequest httpRequest, TelegramOptions options)
{
    string? initData = null;
    if (httpRequest.Headers.TryGetValue("X-Telegram-Init-Data", out var header)
        && !string.IsNullOrWhiteSpace(header))
    {
        initData = header.ToString();
    }
    else if (httpRequest.Query.TryGetValue("initData", out var q) && !string.IsNullOrWhiteSpace(q))
    {
        // Optional query fallback for img tags — not IG token; prefer header via fetch+blob.
        initData = q.ToString();
    }

    var validated = TelegramInitDataValidator.Validate(
        initData,
        options.BotToken,
        options.InitDataMaxAgeSeconds);
    if (!validated.Ok || validated.UserId is null)
    {
        return Results.Problem(
            detail: $"Telegram initData required for media ({validated.ErrorCode ?? "invalid"}).",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized");
    }

    return null;
}

static void EnrichPlanImageUrls(ResearchLatestResponse latest)
{
    if (latest.Items is null || latest.Items.Count == 0)
    {
        return;
    }

    foreach (var item in latest.Items)
    {
        if (string.IsNullOrWhiteSpace(item.MediaPath))
        {
            continue;
        }

        // Gateway proxy only — never expose volume absolute path or IG CDN.
        item.ImageUrl = "/api/miniapp/research/media?path=" + Uri.EscapeDataString(item.MediaPath);
    }
}

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
    public List<VkCommunityTargetDto>? VkCommunities { get; set; }
    public int? CadenceDays { get; set; }
    public string? Timezone { get; set; }
    public string? NotifyChatId { get; set; }
    /// <summary>Optional fallback when header X-Telegram-Init-Data is absent.</summary>
    public string? InitData { get; set; }
}

public sealed class MiniAppResearchRunRequest
{
    public string? UserId { get; set; }
    public string? NotifyChatId { get; set; }
    /// <summary>Additive: null/instagram → IG; vk → VK allowlist.</summary>
    public string? Source { get; set; }
    /// <summary>Optional fallback when header X-Telegram-Init-Data is absent.</summary>
    public string? InitData { get; set; }
}

public partial class Program;
