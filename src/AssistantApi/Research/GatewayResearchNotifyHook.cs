using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssistantApi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantApi.Research;

/// <summary>
/// Posts research status/plan text to telegram-gateway internal notify (bot token stays in gateway).
/// Soft-fails: never throws to scheduler.
/// </summary>
public sealed class GatewayResearchNotifyHook : IResearchNotifyHook
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly IResearchArtifactStore _artifacts;
    private readonly ILogger<GatewayResearchNotifyHook> _logger;

    public GatewayResearchNotifyHook(
        HttpClient http,
        IOptions<GatewayNotifyOptions> options,
        IResearchArtifactStore artifacts,
        ILogger<GatewayResearchNotifyHook> logger)
    {
        _http = http;
        _artifacts = artifacts;
        _logger = logger;
        var o = options.Value;
        _http.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 2, 60));
        _http.DefaultRequestHeaders.Remove("X-Service-Key");
        if (!string.IsNullOrWhiteSpace(o.ServiceKey))
        {
            _http.DefaultRequestHeaders.Add("X-Service-Key", o.ServiceKey);
        }
    }

    public async Task OnResearchRunAsync(ResearchNotifyEvent evt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(evt.NotifyChatId) || !long.TryParse(evt.NotifyChatId, out var chatId))
        {
            return;
        }

        string? planPreview = null;
        try
        {
            var plan = await _artifacts.GetLatestPlanAsync(evt.UserId, cancellationToken);
            planPreview = ResearchPlanPreview.Format(plan, maxItems: 3);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research notify plan preview failed userId={UserId}", evt.UserId);
        }

        var text = evt.Success
            ? $"Research OK (period {evt.PeriodKey})."
            : $"Research failed (period {evt.PeriodKey}): {evt.ErrorCode ?? "error"}.";

        if (!string.IsNullOrWhiteSpace(planPreview))
        {
            text += "\n\n" + planPreview;
        }

        if (text.Length > 3500)
        {
            text = text[..3500] + "…";
        }

        try
        {
            using var response = await _http.PostAsJsonAsync(
                "internal/notify",
                new { chatId, text },
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Gateway research notify failed status={StatusCode} userId={UserId}",
                    (int)response.StatusCode,
                    evt.UserId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Gateway research notify transport failed userId={UserId}", evt.UserId);
        }
    }
}
