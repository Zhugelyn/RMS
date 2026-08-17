using AssistantApi.Contracts;
using AssistantApi.Harness;
using AssistantApi.Memory;
using AssistantApi.Options;
using AssistantApi.Packs;
using AssistantApi.Providers;
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

var cursorSection = builder.Configuration.GetSection(CursorOptions.SectionName);
var hasCursorKeyMaterial =
    !string.IsNullOrWhiteSpace(cursorSection["ApiKey"]) ||
    !string.IsNullOrWhiteSpace(cursorSection["EncryptedApiKey"]);

if (hasCursorKeyMaterial)
{
    builder.Services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
    builder.Services.AddSingleton<ICursorApiKeyStore, EncryptedCursorApiKeyStore>();
}
else
{
    builder.Services.AddSingleton<ICursorApiKeyStore, EmptyCursorApiKeyStore>();
}

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
builder.Services.AddSingleton<IHarnessMemoryStore, InMemoryHarnessMemoryStore>();
builder.Services.AddSingleton<StubLlmProvider>();
builder.Services.AddSingleton<CursorSdkLlmProvider>();
builder.Services.AddSingleton<ILlmProvider, FallbackLlmProvider>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

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
app.MapHealthChecks("/health/ready");

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
        (text.Contains("sk-", StringComparison.OrdinalIgnoreCase) && text.Length > 20))
    {
        return true;
    }

    return text.Contains("api_key=", StringComparison.OrdinalIgnoreCase)
           || text.Contains("apikey=", StringComparison.OrdinalIgnoreCase);
}

public partial class Program;
