using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TelegramGateway.Contracts;
using TelegramGateway.Options;

namespace TelegramGateway.Services;

public interface IAssistantApiClient
{
    Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken);
}

public sealed class AssistantApiClient : IAssistantApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<AssistantApiClient> _logger;

    public AssistantApiClient(HttpClient httpClient, IOptions<AssistantClientOptions> options, ILogger<AssistantApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Remove("X-Service-Key");
        _httpClient.DefaultRequestHeaders.Add("X-Service-Key", options.Value.ServiceKey);
    }

    public async Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("v1/chat", request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "assistant-api chat failed status={StatusCode} traceId={TraceId}",
                (int)response.StatusCode,
                request.TraceId);
            response.EnsureSuccessStatusCode();
        }

        var body = await response.Content.ReadFromJsonAsync<AssistantChatResponse>(JsonOptions, cancellationToken);
        if (body is null)
        {
            throw new InvalidOperationException("Empty assistant-api response.");
        }

        return body;
    }
}
