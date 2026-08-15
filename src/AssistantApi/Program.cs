using AssistantApi.Contracts;
using AssistantApi.Options;
using AssistantApi.Providers;
using AssistantApi.Security;
using AssistantApi.Services;
using Microsoft.AspNetCore.Mvc;

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

builder.Services.AddSingleton<ILlmProvider, StubLlmProvider>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

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
    // Reject obvious Cursor/API key patterns from chat payloads.
    if (text.Contains("CURSOR_API_KEY", StringComparison.OrdinalIgnoreCase) ||
        (text.Contains("sk-", StringComparison.OrdinalIgnoreCase) && text.Length > 20))
    {
        return true;
    }

    return text.Contains("api_key=", StringComparison.OrdinalIgnoreCase)
           || text.Contains("apikey=", StringComparison.OrdinalIgnoreCase);
}

public partial class Program;
