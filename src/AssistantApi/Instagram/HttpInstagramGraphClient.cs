using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AssistantApi.Options;
using AssistantApi.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantApi.Instagram;

/// <summary>
/// Instagram Graph API client for the configured own account only (ADR-009). No Apify.
/// Soft-fails on rate-limit / token expiry without leaking secrets.
/// </summary>
public sealed class HttpInstagramGraphClient : IInstagramGraphClient
{
    private const string MediaFields = "id,caption,media_type,media_url,permalink,thumbnail_url,timestamp";
    private const string InsightsMetrics = "impressions,reach,engagement,saved,video_views";

    private readonly HttpClient _http;
    private readonly IInstagramTokenStore _tokenStore;
    private readonly InstagramOptions _options;
    private readonly ILogger<HttpInstagramGraphClient> _logger;

    public HttpInstagramGraphClient(
        HttpClient http,
        IInstagramTokenStore tokenStore,
        IOptions<InstagramOptions> options,
        ILogger<HttpInstagramGraphClient> logger)
    {
        _http = http;
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;

        var baseUrl = string.IsNullOrWhiteSpace(_options.GraphBaseUrl)
            ? "https://graph.facebook.com/v21.0/"
            : _options.GraphBaseUrl.TrimEnd('/') + "/";
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 5, 120));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsConfigured =>
        _tokenStore.HasToken && !string.IsNullOrWhiteSpace(_options.ResolveAccountId());

    public async Task<InstagramMediaFetchResult> FetchOwnMediaAsync(
        int? limit = null,
        bool includeInsights = true,
        CancellationToken cancellationToken = default)
    {
        if (!_tokenStore.TryGetAccessToken(out var token))
        {
            return InstagramMediaFetchResult.SkipNoToken();
        }

        var accountId = _options.ResolveAccountId();
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return InstagramMediaFetchResult.SkipNoAccountId();
        }

        var pageLimit = Math.Clamp(
            limit ?? _options.DefaultMediaLimit,
            1,
            InstagramFetchLimits.MaxMediaFetch);
        var path =
            $"{Uri.EscapeDataString(accountId)}/media" +
            $"?fields={MediaFields}&limit={pageLimit}&access_token={Uri.EscapeDataString(token)}";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(path, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Instagram Graph media fetch timed out");
            return InstagramMediaFetchResult.Soft(
                InstagramFetchStatus.SoftError,
                "instagram-timeout",
                "Instagram Graph request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Instagram Graph media fetch transport error");
            return InstagramMediaFetchResult.Soft(
                InstagramFetchStatus.SoftError,
                "instagram-transport",
                "Instagram Graph transport error.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return MapHttpFailure(response.StatusCode, body);
            }

            IReadOnlyList<InstagramMediaItem> items;
            try
            {
                items = InstagramMediaMapper.MapMediaList(body);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Instagram Graph media JSON parse failed");
                return InstagramMediaFetchResult.Soft(
                    InstagramFetchStatus.SoftError,
                    "instagram-parse",
                    "Instagram Graph response could not be parsed.");
            }

            var insightsAttempted = false;
            var insightsAvailable = false;
            if (includeInsights && items.Count > 0)
            {
                insightsAttempted = true;
                var enriched = new List<InstagramMediaItem>(items.Count);
                foreach (var item in items)
                {
                    var insights = await TryFetchInsightsAsync(item.Id, token, cancellationToken);
                    if (insights is not null)
                    {
                        insightsAvailable = true;
                        enriched.Add(InstagramMediaMapper.WithInsights(item, insights));
                    }
                    else
                    {
                        enriched.Add(item);
                    }
                }

                items = enriched;
            }

            return new InstagramMediaFetchResult
            {
                Status = InstagramFetchStatus.Ok,
                Items = items,
                InsightsAttempted = insightsAttempted,
                InsightsAvailable = insightsAvailable
            };
        }
    }

    private async Task<InstagramMediaInsights?> TryFetchInsightsAsync(
        string mediaId,
        string token,
        CancellationToken cancellationToken)
    {
        var path =
            $"{Uri.EscapeDataString(mediaId)}/insights" +
            $"?metric={InsightsMetrics}&access_token={Uri.EscapeDataString(token)}";

        try
        {
            using var response = await _http.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Optional: missing insights scope / unsupported media type → soft skip, no secret leak.
                _logger.LogDebug(
                    "Instagram insights unavailable for media (status {Status})",
                    (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return InstagramMediaMapper.MapInsights(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Instagram insights fetch soft-failed");
            return null;
        }
    }

    private InstagramMediaFetchResult MapHttpFailure(HttpStatusCode status, string body)
    {
        var (code, message, mapped) = ClassifyGraphError(status, body);
        // Never log raw body — may contain token fragments in error URLs.
        _logger.LogWarning(
            "Instagram Graph media fetch soft-failed: status={Status} code={Code}",
            (int)status,
            code);

        return InstagramMediaFetchResult.Soft(mapped, code, message);
    }

    public static (string Code, string Message, InstagramFetchStatus Status) ClassifyGraphError(
        HttpStatusCode status,
        string body)
    {
        var graphCode = TryReadGraphErrorCode(body);
        var graphType = TryReadGraphErrorType(body);
        var graphMessage = TryReadGraphErrorMessage(body);

        if (status == HttpStatusCode.TooManyRequests ||
            graphCode is 4 or 17 or 32 or 613)
        {
            return ("instagram-rate-limited",
                "Instagram Graph rate limit reached. Try again later.",
                InstagramFetchStatus.RateLimited);
        }

        if (status == HttpStatusCode.Unauthorized ||
            status == HttpStatusCode.Forbidden ||
            graphCode is 190 or 102 ||
            (!string.IsNullOrEmpty(graphType) &&
             graphType.Contains("OAuthException", StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(graphMessage) &&
             (graphMessage.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
              graphMessage.Contains("session has been invalidated", StringComparison.OrdinalIgnoreCase) ||
              graphMessage.Contains("access token", StringComparison.OrdinalIgnoreCase))))
        {
            return ("instagram-token-expired",
                "Instagram Graph access token is invalid or expired.",
                InstagramFetchStatus.TokenExpired);
        }

        return ("instagram-graph-error",
            "Instagram Graph request failed.",
            InstagramFetchStatus.SoftError);
    }

    private static int? TryReadGraphErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("code", out var code) &&
                code.TryGetInt32(out var n))
            {
                return n;
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static string? TryReadGraphErrorType(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("type", out var type))
            {
                return type.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static string? TryReadGraphErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
            {
                return msg.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }
}

/// <summary>Honest stub when Instagram token is not configured. Compose stays green.</summary>
public sealed class StubInstagramGraphClient : IInstagramGraphClient
{
    public bool IsConfigured => false;

    public Task<InstagramMediaFetchResult> FetchOwnMediaAsync(
        int? limit = null,
        bool includeInsights = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InstagramMediaFetchResult.SkipNoToken());
}

/// <summary>
/// Chooses live Graph client when token+account present; otherwise stub skip.
/// </summary>
public sealed class FallbackInstagramGraphClient : IInstagramGraphClient
{
    private readonly IInstagramTokenStore _tokenStore;
    private readonly InstagramOptions _options;
    private readonly HttpInstagramGraphClient _live;
    private readonly StubInstagramGraphClient _stub;

    public FallbackInstagramGraphClient(
        IInstagramTokenStore tokenStore,
        IOptions<InstagramOptions> options,
        HttpInstagramGraphClient live,
        StubInstagramGraphClient stub)
    {
        _tokenStore = tokenStore;
        _options = options.Value;
        _live = live;
        _stub = stub;
    }

    public bool IsConfigured =>
        _tokenStore.HasToken && !string.IsNullOrWhiteSpace(_options.ResolveAccountId());

    public Task<InstagramMediaFetchResult> FetchOwnMediaAsync(
        int? limit = null,
        bool includeInsights = true,
        CancellationToken cancellationToken = default)
    {
        if (!_tokenStore.HasToken)
        {
            return _stub.FetchOwnMediaAsync(limit, includeInsights, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(_options.ResolveAccountId()))
        {
            return Task.FromResult(InstagramMediaFetchResult.SkipNoAccountId());
        }

        return _live.FetchOwnMediaAsync(limit, includeInsights, cancellationToken);
    }
}
