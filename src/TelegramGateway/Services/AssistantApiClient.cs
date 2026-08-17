using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TelegramGateway.Contracts;
using TelegramGateway.Options;

namespace TelegramGateway.Services;

public interface IAssistantApiClient
{
    Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken);
    Task<ResearchSettingsDto> GetResearchSettingsAsync(string userId, CancellationToken cancellationToken);
    Task<ResearchSettingsDto> PutResearchSettingsAsync(ResearchSettingsUpdateRequest request, CancellationToken cancellationToken);
    Task<ResearchRunResponse> RunResearchAsync(ResearchRunRequest request, CancellationToken cancellationToken);
    Task<ResearchLatestResponse> GetResearchLatestAsync(string userId, CancellationToken cancellationToken);
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

    public async Task<ResearchSettingsDto> GetResearchSettingsAsync(string userId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"v1/research/settings?userId={Uri.EscapeDataString(userId)}",
            cancellationToken);
        await EnsureOkAsync(response, "research settings get", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResearchSettingsDto>(JsonOptions, cancellationToken))
               ?? new ResearchSettingsDto { UserId = userId };
    }

    public async Task<ResearchSettingsDto> PutResearchSettingsAsync(
        ResearchSettingsUpdateRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PutAsJsonAsync("v1/research/settings", request, JsonOptions, cancellationToken);
        await EnsureOkAsync(response, "research settings put", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResearchSettingsDto>(JsonOptions, cancellationToken))
               ?? new ResearchSettingsDto { UserId = request.UserId };
    }

    public async Task<ResearchRunResponse> RunResearchAsync(ResearchRunRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("v1/research/run", request, JsonOptions, cancellationToken);
        await EnsureOkAsync(response, "research run", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions, cancellationToken))
               ?? new ResearchRunResponse { Outcome = "Unknown", Settings = new ResearchSettingsDto { UserId = request.UserId } };
    }

    public async Task<ResearchLatestResponse> GetResearchLatestAsync(string userId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"v1/research/latest?userId={Uri.EscapeDataString(userId)}",
            cancellationToken);
        await EnsureOkAsync(response, "research latest", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResearchLatestResponse>(JsonOptions, cancellationToken))
               ?? new ResearchLatestResponse { Settings = new ResearchSettingsDto { UserId = userId } };
    }

    private async Task EnsureOkAsync(HttpResponseMessage response, string op, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "assistant-api {Op} failed status={StatusCode}",
            op,
            (int)response.StatusCode);
        throw new HttpRequestException(
            $"assistant-api {op} failed ({(int)response.StatusCode}): {TrimDetail(detail)}",
            null,
            response.StatusCode);
    }

    private static string TrimDetail(string detail) =>
        detail.Length <= 240 ? detail : detail[..240];
}
