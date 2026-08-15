using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Providers;

/// <summary>HTTP client to internal cursor-sdk-bridge (@cursor/sdk).</summary>
public sealed class HttpCursorSdkClient : ICursorSdkClient
{
    private readonly HttpClient _httpClient;
    private readonly CursorOptions _options;
    private readonly ILogger<HttpCursorSdkClient> _logger;

    public HttpCursorSdkClient(HttpClient httpClient, IOptions<CursorOptions> options, ILogger<HttpCursorSdkClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CursorSdkRunResult> RunAsync(CursorSdkRunRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/run")
        {
            Content = JsonContent.Create(new BridgeRunRequest
            {
                ApiKey = request.ApiKey,
                Prompt = request.Prompt,
                AgentId = request.AgentId,
                Model = request.Model,
                PackId = request.PackId
            })
        };

        _logger.LogInformation(
            "Cursor SDK bridge run model={Model} packId={PackId} resume={Resume} promptLength={PromptLength}",
            request.Model,
            request.PackId,
            !string.IsNullOrWhiteSpace(request.AgentId),
            request.Prompt.Length);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            detail = Scrub(detail, request.ApiKey);
            throw new InvalidOperationException($"Cursor SDK bridge failed: {(int)response.StatusCode} {detail}");
        }

        var body = await response.Content.ReadFromJsonAsync<BridgeRunResponse>(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("Cursor SDK bridge returned empty body.");

        if (string.IsNullOrWhiteSpace(body.AgentId) || string.IsNullOrWhiteSpace(body.Text))
        {
            throw new InvalidOperationException("Cursor SDK bridge response missing agentId/text.");
        }

        return new CursorSdkRunResult(body.AgentId, body.Text, body.PackId ?? request.PackId);
    }

    private static string Scrub(string detail, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(detail))
        {
            return detail;
        }

        return detail.Replace(apiKey, "***", StringComparison.Ordinal);
    }

    private sealed class BridgeRunRequest
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("agentId")]
        public string? AgentId { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("packId")]
        public string? PackId { get; set; }
    }

    private sealed class BridgeRunResponse
    {
        [JsonPropertyName("agentId")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("packId")]
        public string? PackId { get; set; }
    }
}
