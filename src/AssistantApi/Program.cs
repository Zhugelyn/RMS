using AssistantApi.Contracts;
using AssistantApi.Data;
using AssistantApi.Harness;
using AssistantApi.Instagram;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Providers;
using AssistantApi.Research;
using AssistantApi.Security;
using AssistantApi.Services;
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

var cursorSection = builder.Configuration.GetSection(CursorOptions.SectionName);
var instagramSection = builder.Configuration.GetSection(InstagramOptions.SectionName);
var hasCursorKeyMaterial =
    !string.IsNullOrWhiteSpace(cursorSection["ApiKey"]) ||
    !string.IsNullOrWhiteSpace(cursorSection["EncryptedApiKey"]);
var hasInstagramTokenMaterial =
    !string.IsNullOrWhiteSpace(instagramSection["AccessToken"]) ||
    !string.IsNullOrWhiteSpace(instagramSection["EncryptedAccessToken"]);

var resolvedMasterKey =
    !string.IsNullOrWhiteSpace(instagramSection["MasterKey"]) ? instagramSection["MasterKey"]!
    : !string.IsNullOrWhiteSpace(cursorSection["MasterKey"]) ? cursorSection["MasterKey"]!
    : string.Empty;

if ((hasCursorKeyMaterial || hasInstagramTokenMaterial) &&
    (string.IsNullOrWhiteSpace(resolvedMasterKey) || resolvedMasterKey.Length < 16))
{
    throw new InvalidOperationException(
        "A master key (≥16 chars) is required when Cursor or Instagram secrets are set " +
        "(Cursor:MasterKey and/or Instagram:MasterKey).");
}

if (hasCursorKeyMaterial || hasInstagramTokenMaterial)
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
builder.Services
    .AddOptions<ResearchSchedulerOptions>()
    .Bind(builder.Configuration.GetSection(ResearchSchedulerOptions.SectionName));
builder.Services.AddSingleton<IResearchNotifyHook, NoOpResearchNotifyHook>();
builder.Services.AddSingleton<IResearchSchedulerJob, ResearchSchedulerJob>();
builder.Services.AddHostedService<ResearchSchedulerHostedService>();
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

app.Run();

static bool LooksLikeSecret(string text)
{
    if (text.Contains("CURSOR_API_KEY", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("INSTAGRAM__ACCESSTOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("INSTAGRAM_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("IG_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
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
           || text.Contains("access_token=", StringComparison.OrdinalIgnoreCase);
}

public partial class Program;
