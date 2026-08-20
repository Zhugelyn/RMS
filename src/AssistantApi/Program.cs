using AssistantApi.Contracts;
using AssistantApi.Data;
using AssistantApi.Files;
using AssistantApi.Harness;
using AssistantApi.Instagram;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Providers;
using AssistantApi.Rag;
using AssistantApi.Research;
using AssistantApi.Security;
using AssistantApi.Services;
using AssistantApi.Vk;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

builder.Services
    .AddOptions<ServiceAuthOptions>()
    .Bind(builder.Configuration.GetSection(ServiceAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<CursorOptions>()
    .Bind(builder.Configuration.GetSection(CursorOptions.SectionName))
    .Validate(o => string.IsNullOrWhiteSpace(o.ApiKey) && string.IsNullOrWhiteSpace(o.EncryptedApiKey)
                   || !string.IsNullOrWhiteSpace(o.MasterKey),
        "Cursor:MasterKey is required when Cursor:ApiKey or Cursor:EncryptedApiKey is set.")
    .Validate(o => string.IsNullOrWhiteSpace(o.MasterKey) || o.MasterKey.Length >= 16,
        "Cursor:MasterKey must be at least 16 characters when set.")
    .ValidateOnStart();

builder.Services
    .AddOptions<InstagramOptions>()
    .Bind(builder.Configuration.GetSection(InstagramOptions.SectionName))
    .Validate(o => string.IsNullOrWhiteSpace(o.MasterKey) || o.MasterKey.Length >= 16,
        "Instagram:MasterKey must be at least 16 characters when set.")
    .ValidateOnStart();

builder.Services
    .AddOptions<VkOptions>()
    .Bind(builder.Configuration.GetSection(VkOptions.SectionName))
    .Validate(o => string.IsNullOrWhiteSpace(o.MasterKey) || o.MasterKey.Length >= 16,
        "Vk:MasterKey must be at least 16 characters when set.")
    .Validate(o => string.IsNullOrWhiteSpace(o.ApiBaseUrl)
                   || VkApiHostGuard.IsAllowedApiBaseUrl(o.ApiBaseUrl, out _, out _),
        "Vk:ApiBaseUrl must be https://api.vk.com/... (no m.vk.com / scrape / OAuth hosts).")
    .ValidateOnStart();

var cursorSection = builder.Configuration.GetSection(CursorOptions.SectionName);
var instagramSection = builder.Configuration.GetSection(InstagramOptions.SectionName);
var vkSection = builder.Configuration.GetSection(VkOptions.SectionName);
var hasCursorKeyMaterial =
    !string.IsNullOrWhiteSpace(cursorSection["ApiKey"]) ||
    !string.IsNullOrWhiteSpace(cursorSection["EncryptedApiKey"]);
var hasInstagramTokenMaterial =
    !string.IsNullOrWhiteSpace(instagramSection["AccessToken"]) ||
    !string.IsNullOrWhiteSpace(instagramSection["EncryptedAccessToken"]);
var hasVkTokenMaterial =
    !string.IsNullOrWhiteSpace(vkSection["ServiceToken"]) ||
    !string.IsNullOrWhiteSpace(vkSection["EncryptedServiceToken"]);

var resolvedMasterKey =
    !string.IsNullOrWhiteSpace(vkSection["MasterKey"]) ? vkSection["MasterKey"]!
    : !string.IsNullOrWhiteSpace(instagramSection["MasterKey"]) ? instagramSection["MasterKey"]!
    : !string.IsNullOrWhiteSpace(cursorSection["MasterKey"]) ? cursorSection["MasterKey"]!
    : string.Empty;

if ((hasCursorKeyMaterial || hasInstagramTokenMaterial || hasVkTokenMaterial) &&
    (string.IsNullOrWhiteSpace(resolvedMasterKey) || resolvedMasterKey.Length < 16))
{
    throw new InvalidOperationException(
        "A master key (≥16 chars) is required when Cursor, Instagram, or VK secrets are set " +
        "(Cursor:MasterKey and/or Instagram:MasterKey and/or Vk:MasterKey).");
}

if (hasCursorKeyMaterial || hasInstagramTokenMaterial || hasVkTokenMaterial)
{
    builder.Services.AddSingleton<ISecretProtector>(_ => new AesGcmSecretProtector(resolvedMasterKey));
}

if (hasCursorKeyMaterial)
{
    builder.Services.AddSingleton<ICursorApiKeyStore, EncryptedCursorApiKeyStore>();
}
else
{
    builder.Services.AddSingleton<ICursorApiKeyStore, EmptyCursorApiKeyStore>();
}

if (hasInstagramTokenMaterial)
{
    builder.Services.AddSingleton<IInstagramTokenStore, EncryptedInstagramTokenStore>();
}
else
{
    builder.Services.AddSingleton<IInstagramTokenStore, EmptyInstagramTokenStore>();
}

if (hasVkTokenMaterial)
{
    builder.Services.AddSingleton<IVkTokenStore, EncryptedVkTokenStore>();
}
else
{
    builder.Services.AddSingleton<IVkTokenStore, EmptyVkTokenStore>();
}

builder.Services.AddSingleton<StubInstagramGraphClient>();
builder.Services.AddHttpClient<HttpInstagramGraphClient>();
builder.Services.AddHttpClient<IInstagramMediaDownloader, InstagramMediaDownloader>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // SSRF: never auto-follow; caller must re-check allowlist per hop if following manually.
        AllowAutoRedirect = false
    });
builder.Services.AddTransient<FallbackInstagramGraphClient>();
builder.Services.AddTransient<IInstagramGraphClient>(sp => sp.GetRequiredService<FallbackInstagramGraphClient>());

builder.Services.AddSingleton<StubVkWallClient>();
builder.Services.AddHttpClient<HttpVkWallClient>();
builder.Services.AddHttpClient<IVkMediaDownloader, VkMediaDownloader>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // SSRF: never auto-follow; caller re-checks allowlist if following manually.
        AllowAutoRedirect = false
    });
builder.Services.AddTransient<FallbackVkWallClient>();
builder.Services.AddTransient<IVkWallClient>(sp => sp.GetRequiredService<FallbackVkWallClient>());
builder.Services.AddSingleton<IVkPhotoStore, VkPhotoStore>();

builder.Services
    .AddOptions<AgentPacksOptions>()
    .Bind(builder.Configuration.GetSection(AgentPacksOptions.SectionName));

builder.Services.AddSingleton<IPackCatalog>(sp =>
{
    var options = sp.GetRequiredService<IOptions<AgentPacksOptions>>();
    var env = sp.GetRequiredService<IHostEnvironment>();
    return PackCatalog.LoadFromOptions(options, env.ContentRootPath);
});

builder.Services.AddSingleton<IDomainHarness>(sp => new DomainHarness(sp.GetRequiredService<IPackCatalog>()));
builder.Services.AddSingleton<IPackPromptBuilder, PackPromptBuilder>();
builder.Services.AddSingleton<IAgentAffinityStore, InMemoryAgentAffinityStore>();
builder.Services.AddAssistantPersistence(builder.Configuration);
builder.Services.AddSingleton<IResearchPackInjector, ResearchPackInjector>();
builder.Services.AddSingleton<IInstagramResearchCapture, InstagramResearchCapture>();
builder.Services.AddSingleton<IVkResearchCapture, VkResearchCapture>();
builder.Services
    .AddOptions<ResearchSchedulerOptions>()
    .Bind(builder.Configuration.GetSection(ResearchSchedulerOptions.SectionName));
builder.Services
    .AddOptions<ResearchOptions>()
    .Bind(builder.Configuration.GetSection(ResearchOptions.SectionName));
builder.Services
    .AddOptions<GatewayNotifyOptions>()
    .Bind(builder.Configuration.GetSection(GatewayNotifyOptions.SectionName));

// Phase 7 pack retriever: assistant-api → rag-service HTTP (never ES). Soft-fail when unset/down.
builder.Services
    .AddOptions<RagOptions>()
    .Bind(builder.Configuration.GetSection(RagOptions.SectionName))
    .Validate(
        o => string.IsNullOrWhiteSpace(o.BaseUrl)
             || (!string.IsNullOrWhiteSpace(o.ServiceKey) && o.ServiceKey.Length >= 16),
        "Rag:ServiceKey (≥16 chars) is required when Rag:BaseUrl is set.")
    .ValidateOnStart();

var ragBaseUrl = builder.Configuration.GetSection(RagOptions.SectionName)["BaseUrl"];
if (!string.IsNullOrWhiteSpace(ragBaseUrl))
{
    builder.Services.AddHttpClient<IRagRetriever, HttpRagRetriever>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<RagOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
    });
}
else
{
    builder.Services.AddSingleton<IRagRetriever, NoOpRagRetriever>();
}

builder.Services.AddSingleton<IRagPackInjector, RagPackInjector>();

// Phase 8 Files/MinIO: metadata + short-TTL presign. Empty Endpoint → UnavailablePresigner; chat unaffected.
builder.Services
    .AddOptions<MinioOptions>()
    .Bind(builder.Configuration.GetSection(MinioOptions.SectionName))
    .Validate(
        o => !o.IsConfigured
             || (o.AccessKey.Length >= 3 && o.SecretKey.Length >= 8),
        "Minio:AccessKey/SecretKey required when Minio:Endpoint is set.")
    .ValidateOnStart();

var minioEndpoint = builder.Configuration.GetSection(MinioOptions.SectionName)["Endpoint"];
var minioAccess = builder.Configuration.GetSection(MinioOptions.SectionName)["AccessKey"];
var minioSecret = builder.Configuration.GetSection(MinioOptions.SectionName)["SecretKey"];
if (!string.IsNullOrWhiteSpace(minioEndpoint)
    && !string.IsNullOrWhiteSpace(minioAccess)
    && !string.IsNullOrWhiteSpace(minioSecret))
{
    builder.Services.AddSingleton<IObjectStoragePresigner, MinioObjectStoragePresigner>();
}
else
{
    builder.Services.AddSingleton<IObjectStoragePresigner, UnavailableObjectStoragePresigner>();
}

builder.Services.AddSingleton<IFileContentScanner, PassThroughFileContentScanner>();
builder.Services.AddSingleton<IFilePresignService, FilePresignService>();
builder.Services.AddSingleton<IFilesPackInjector, FilesPackInjector>();

builder.Services.AddSingleton<IResearchImageWorkspace, ResearchImageWorkspace>();
builder.Services.AddSingleton<IResearchImageGenerator, ResearchImageGenerator>();

var gatewayNotifyBase = builder.Configuration.GetSection(GatewayNotifyOptions.SectionName)["BaseUrl"];
if (!string.IsNullOrWhiteSpace(gatewayNotifyBase))
{
    builder.Services.AddHttpClient<IResearchNotifyHook, GatewayResearchNotifyHook>();
}
else
{
    builder.Services.AddSingleton<IResearchNotifyHook, NoOpResearchNotifyHook>();
}

builder.Services.AddSingleton<IResearchSchedulerJob, ResearchSchedulerJob>();
builder.Services.AddHostedService<ResearchSchedulerHostedService>();
builder.Services.AddSingleton<IResearchApiService, ResearchApiService>();
builder.Services.AddSingleton<StubLlmProvider>();
builder.Services.AddSingleton<CursorSdkLlmProvider>();
builder.Services.AddSingleton<ILlmProvider, FallbackLlmProvider>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddProblemDetails();

var healthChecks = builder.Services.AddHealthChecks();
if (PersistenceRegistration.HasPostgres(builder.Configuration))
{
    // Honest fail ready when ConnectionStrings:AssistantDb is set but DB is unreachable.
    healthChecks.AddCheck<AssistantDbHealthCheck>("assistant-db", tags: new[] { "ready" });
}

builder.Services.AddHttpClient<ICursorSdkClient, HttpCursorSdkClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<CursorOptions>>().Value;
    client.BaseAddress = new Uri(options.BridgeBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});

var app = builder.Build();

// Phase 3: validate packs at startup.
_ = app.Services.GetRequiredService<IPackCatalog>();

app.UseMiddleware<ServiceKeyAuthMiddleware>();

app.MapHealthChecks("/health/live");
if (PersistenceRegistration.HasPostgres(builder.Configuration))
{
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });
}
else
{
    // No Postgres: in-process stores; ready stays green without a DB probe.
    app.MapHealthChecks("/health/ready");
}

app.MapPost("/v1/chat", async (
    [FromBody] ChatRequest request,
    ChatService chatService,
    CancellationToken cancellationToken) =>
{
    if (request.SchemaVersion != 1)
    {
        return Results.Problem(
            detail: "Unsupported schemaVersion. Expected 1.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid schemaVersion");
    }

    if (string.IsNullOrWhiteSpace(request.ConversationId) ||
        string.IsNullOrWhiteSpace(request.UserId) ||
        string.IsNullOrWhiteSpace(request.Text) ||
        string.IsNullOrWhiteSpace(request.TraceId))
    {
        return Results.Problem(
            detail: "conversationId, userId, text and traceId are required.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    if (LooksLikeSecret(request.Text))
    {
        return Results.Problem(
            detail: "Secrets and API keys must not be sent in chat text.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Secret rejected");
    }

    var response = await chatService.ChatAsync(request, cancellationToken);
    return Results.Ok(response);
})
.WithName("Chat");

// Phase 4 Mini App / bot research — owner = assistant-api. No IG token in DTO/response.
app.MapGet("/v1/research/settings", async (
    [FromQuery] string? userId,
    IResearchApiService research,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(userId))
    {
        return Results.Problem(
            detail: "userId must be tg-<telegramUserId>.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    var dto = await research.GetSettingsAsync(userId!, cancellationToken);
    return Results.Ok(dto);
})
.WithName("ResearchSettingsGet");

app.MapPut("/v1/research/settings", async (
    [FromBody] ResearchSettingsUpdateRequest request,
    IResearchApiService research,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(request.UserId))
    {
        return Results.Problem(
            detail: "userId must be tg-<telegramUserId>. Anonymous writes rejected.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    if (LooksLikeSecret(request.InstagramHandle ?? string.Empty) ||
        LooksLikeSecret(request.Timezone ?? string.Empty) ||
        LooksLikeSecret(request.NotifyChatId ?? string.Empty) ||
        VkCommunitiesLookLikeSecret(request.VkCommunities))
    {
        return Results.Problem(
            detail: "Secrets and Instagram/VK tokens must not be sent in research settings.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Secret rejected");
    }

    try
    {
        var dto = await research.UpsertSettingsAsync(request, cancellationToken);
        return Results.Ok(dto);
    }
    catch (ResearchValidationException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Validation failed");
    }
})
.WithName("ResearchSettingsPut");

app.MapPost("/v1/research/run", async (
    [FromBody] ResearchRunRequest request,
    IResearchApiService research,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(request.UserId))
    {
        return Results.Problem(
            detail: "userId must be tg-<telegramUserId>. Anonymous runs rejected.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    try
    {
        var result = await research.RunNowAsync(request, cancellationToken);
        return Results.Ok(result);
    }
    catch (ResearchValidationException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Validation failed");
    }
})
.WithName("ResearchRun");

app.MapGet("/v1/research/latest", async (
    [FromQuery] string? userId,
    IResearchApiService research,
    CancellationToken cancellationToken) =>
{
    if (!IsTelegramUserId(userId))
    {
        return Results.Problem(
            detail: "userId must be tg-<telegramUserId>.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed");
    }

    var latest = await research.GetLatestAsync(userId!, cancellationToken);
    return Results.Ok(latest);
})
.WithName("ResearchLatest");

// Phase 8 Files (ADR-015): metadata + presigned PUT/GET. No large byte proxy. Auth = X-Service-Key
// (gateway / pack). Mini App initData HMAC — when UI proxy is added (gateway), not in this slice.
app.MapPost("/v1/files/upload-intent", async (
    [FromBody] FileUploadIntentRequest request,
    IFilePresignService files,
    CancellationToken cancellationToken) =>
{
    if (LooksLikeSecret(request.OriginalFilename ?? string.Empty)
        || LooksLikeSecret(request.ContentType ?? string.Empty)
        || LooksLikeSecret(request.Domain ?? string.Empty))
    {
        return Results.Problem(
            detail: "Secrets must not be sent in file metadata.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Secret rejected");
    }

    try
    {
        var result = await files.CreateUploadIntentAsync(request, cancellationToken);
        return Results.Ok(result);
    }
    catch (FileValidationException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Validation failed");
    }
    catch (FileStorageUnavailableException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage unavailable");
    }
})
.WithName("FilesUploadIntent");

app.MapPost("/v1/files/{fileId:guid}/confirm", async (
    Guid fileId,
    [FromBody] FileConfirmRequest request,
    IFilePresignService files,
    CancellationToken cancellationToken) =>
{
    try
    {
        var dto = await files.ConfirmUploadAsync(fileId, request, cancellationToken);
        return Results.Ok(dto);
    }
    catch (FileValidationException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Validation failed");
    }
    catch (FileObjectNotFoundException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Not found");
    }
    catch (FileForbiddenException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");
    }
})
.WithName("FilesConfirm");

app.MapPost("/v1/files/{fileId:guid}/download-url", async (
    Guid fileId,
    [FromBody] FileDownloadUrlRequest request,
    IFilePresignService files,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await files.CreateDownloadUrlAsync(fileId, request, cancellationToken);
        return Results.Ok(result);
    }
    catch (FileValidationException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Validation failed");
    }
    catch (FileObjectNotFoundException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Not found");
    }
    catch (FileForbiddenException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");
    }
    catch (FileStorageUnavailableException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage unavailable");
    }
})
.WithName("FilesDownloadUrl");

app.MapGet("/v1/files/{fileId:guid}", async (
    Guid fileId,
    [FromQuery] string? userId,
    IFilePresignService files,
    CancellationToken cancellationToken) =>
{
    try
    {
        var dto = await files.GetMetadataAsync(fileId, userId ?? string.Empty, cancellationToken);
        return Results.Ok(dto);
    }
    catch (FileValidationException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Validation failed");
    }
    catch (FileObjectNotFoundException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Not found");
    }
})
.WithName("FilesMetadataGet");

app.Run();

static bool IsTelegramUserId(string? userId) =>
    !string.IsNullOrWhiteSpace(userId)
    && userId.StartsWith("tg-", StringComparison.Ordinal)
    && userId.Length > 3
    && userId.Length <= 64
    && userId[3..].All(char.IsDigit);

static bool LooksLikeSecret(string text)
{
    if (text.Contains("CURSOR_API_KEY", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("INSTAGRAM__ACCESSTOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("INSTAGRAM_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("IG_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("VK__SERVICETOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("VK_SERVICE_TOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("VK_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("RAG__SERVICEKEY", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("RAG_SERVICE_KEY", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ELASTICSEARCH__PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ELASTIC_PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("MINIO__SECRETKEY", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("MINIO__ROOTPASSWORD", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("MINIO_SECRET_KEY", StringComparison.OrdinalIgnoreCase) ||
        (text.Contains("sk-", StringComparison.OrdinalIgnoreCase) && text.Length > 20))
    {
        return true;
    }

    // Instagram long-lived user tokens often start with IGQ / EAA / IGT.
    if (text.Contains("IGQVJ", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("IGQWR", StringComparison.OrdinalIgnoreCase) ||
        (text.Contains("EAA", StringComparison.Ordinal) && text.Length > 40))
    {
        return true;
    }

    return text.Contains("api_key=", StringComparison.OrdinalIgnoreCase)
           || text.Contains("apikey=", StringComparison.OrdinalIgnoreCase)
           || text.Contains("access_token=", StringComparison.OrdinalIgnoreCase)
           || text.Contains("service_token=", StringComparison.OrdinalIgnoreCase);
}

static bool VkCommunitiesLookLikeSecret(IReadOnlyList<AssistantApi.Research.VkCommunityTargetDto>? communities)
{
    if (communities is null || communities.Count == 0)
    {
        return false;
    }

    foreach (var c in communities)
    {
        if (LooksLikeSecret(c.ScreenName ?? string.Empty))
        {
            return true;
        }
    }

    return false;
}

public partial class Program;
