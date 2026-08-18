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
                PackId = request.PackId,
                LocalCwd = request.LocalCwd,
                CollectImages = request.CollectImages,
                ImageCap = request.ImageCap
            })
        };

        _logger.LogInformation(
            "Cursor SDK bridge run model={Model} packId={PackId} resume={Resume} collectImages={CollectImages} promptLength={PromptLength}",
            request.Model,
            request.PackId,
            !string.IsNullOrWhiteSpace(request.AgentId),
            request.CollectImages,
            request.Prompt.Length);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            detail = Scrub(detail, request.ApiKey);
            if (request.CollectImages && IsImageToolStatus(response.StatusCode, detail))
            {
                return new CursorSdkRunResult(
                    agentId: request.AgentId ?? "soft-fail",
                    text: string.Empty,
                    packId: request.PackId,
                    images: Array.Empty<string>(),
                    error: ResearchImageLimits.SoftFailErrorCode);
            }

            throw new InvalidOperationException($"Cursor SDK bridge failed: {(int)response.StatusCode} {detail}");
        }

        var body = await response.Content.ReadFromJsonAsync<BridgeRunResponse>(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("Cursor SDK bridge returned empty body.");

        if (string.Equals(body.Error, ResearchImageLimits.SoftFailErrorCode, StringComparison.Ordinal))
        {
            return new CursorSdkRunResult(
                agentId: string.IsNullOrWhiteSpace(body.AgentId) ? "soft-fail" : body.AgentId,
                text: body.Text ?? string.Empty,
                packId: body.PackId ?? request.PackId,
                images: body.Images is null ? Array.Empty<string>() : body.Images,
                error: ResearchImageLimits.SoftFailErrorCode);
        }

        if (string.IsNullOrWhiteSpace(body.AgentId) || string.IsNullOrWhiteSpace(body.Text))
        {
            if (request.CollectImages)
            {
                return new CursorSdkRunResult(
                    agentId: body.AgentId ?? "soft-fail",
                    text: body.Text ?? string.Empty,
                    packId: body.PackId ?? request.PackId,
                    images: body.Images is null ? Array.Empty<string>() : body.Images,
                    error: ResearchImageLimits.SoftFailErrorCode);
            }

            throw new InvalidOperationException("Cursor SDK bridge response missing agentId/text.");
        }

        return new CursorSdkRunResult(
            body.AgentId,
            body.Text,
            body.PackId ?? request.PackId,
            body.Images,
            body.Error);
    }

    private static bool IsImageToolStatus(System.Net.HttpStatusCode status, string detail) =>
        status == System.Net.HttpStatusCode.TooManyRequests
        || detail.Contains("429", StringComparison.Ordinal)
        || detail.Contains("GenerateImage", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("image-tool", StringComparison.OrdinalIgnoreCase);

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

        [JsonPropertyName("localCwd")]
        public string? LocalCwd { get; set; }

        [JsonPropertyName("collectImages")]
        public bool CollectImages { get; set; }

        [JsonPropertyName("imageCap")]
        public int? ImageCap { get; set; }
    }

    private sealed class BridgeRunResponse
    {
        [JsonPropertyName("agentId")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("packId")]
        public string? PackId { get; set; }

        [JsonPropertyName("images")]
        public List<string>? Images { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
