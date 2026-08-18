using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AssistantApi.Options;
using AssistantApi.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantApi.Vk;

/// <summary>
/// Official VK API client for open community walls (ADR-013). No scrape / Apify / m.vk.com.
/// Soft-fails on rate-limit / closed wall / token issues without leaking secrets.
/// </summary>
public sealed class HttpVkWallClient : IVkWallClient
{
    private readonly HttpClient _http;
    private readonly IVkTokenStore _tokenStore;
    private readonly VkOptions _options;
    private readonly ILogger<HttpVkWallClient> _logger;

    public HttpVkWallClient(
        HttpClient http,
        IVkTokenStore tokenStore,
        IOptions<VkOptions> options,
        ILogger<HttpVkWallClient> logger)
    {
        _http = http;
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;

        var baseUrl = string.IsNullOrWhiteSpace(_options.ApiBaseUrl)
            ? "https://api.vk.com/method/"
            : _options.ApiBaseUrl.TrimEnd('/') + "/";
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 5, 120));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsConfigured => _tokenStore.HasToken;

    public async Task<VkResolveResult> ResolveScreenNameAsync(
        string screenName,
        CancellationToken cancellationToken = default)
    {
        if (!_tokenStore.TryGetServiceToken(out var token))
        {
            return VkResolveResult.SkipNoToken();
        }

        var normalized = NormalizeScreenName(screenName);
        if (normalized is null)
        {
            return VkResolveResult.Soft(
                VkFetchStatus.SkippedUnresolved,
                "vk-resolve-invalid",
                "VK screen name is empty or invalid.");
        }

        var path =
            "utils.resolveScreenName" +
            $"?screen_name={Uri.EscapeDataString(normalized)}" +
            $"&access_token={Uri.EscapeDataString(token)}" +
            $"&v={Uri.EscapeDataString(_options.ApiVersion)}";

        var body = await GetJsonAsync(path, cancellationToken);
        if (body is null)
        {
            return VkResolveResult.Soft(
                VkFetchStatus.SoftError,
                "vk-transport",
                "VK API transport error.");
        }

        try
        {
            return VkWallMapper.MapResolve(body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "VK resolveScreenName JSON parse failed");
            return VkResolveResult.Soft(
                VkFetchStatus.SoftError,
                "vk-resolve-parse",
                "VK resolve response could not be parsed.");
        }
    }

    public async Task<VkWallFetchResult> GetWallAsync(
        string? screenName = null,
        long? ownerId = null,
        int? count = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tokenStore.TryGetServiceToken(out var token))
        {
            return VkWallFetchResult.SkipNoToken();
        }

        long resolvedOwner;
        string? resolvedScreen = null;
        if (ownerId is long oid && oid != 0)
        {
            resolvedOwner = oid;
        }
        else if (!string.IsNullOrWhiteSpace(screenName))
        {
            resolvedScreen = NormalizeScreenName(screenName);
            var resolve = await ResolveScreenNameAsync(screenName, cancellationToken);
            if (resolve.Status != VkFetchStatus.Ok || resolve.OwnerId is null)
            {
                return new VkWallFetchResult
                {
                    Status = resolve.Status == VkFetchStatus.Ok
                        ? VkFetchStatus.SkippedUnresolved
                        : resolve.Status,
                    ErrorCode = resolve.ErrorCode ?? "vk-resolve-failed",
                    Message = resolve.Message ?? "VK screen name could not be resolved.",
                    ScreenName = resolvedScreen
                };
            }

            resolvedOwner = resolve.OwnerId.Value;
        }
        else
        {
            return VkWallFetchResult.Soft(
                VkFetchStatus.SoftError,
                "vk-owner-missing",
                "VK wall fetch requires screenName or ownerId.");
        }

        var pageCount = Math.Clamp(
            count ?? _options.DefaultWallCount,
            1,
            VkFetchLimits.MaxWallFetch);

        var path =
            "wall.get" +
            $"?owner_id={resolvedOwner}" +
            $"&count={pageCount}" +
            "&filter=owner" +
            $"&access_token={Uri.EscapeDataString(token)}" +
            $"&v={Uri.EscapeDataString(_options.ApiVersion)}";

        var body = await GetJsonAsync(path, cancellationToken);
        if (body is null)
        {
            return VkWallFetchResult.Soft(
                VkFetchStatus.SoftError,
                "vk-transport",
                "VK API transport error.");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (VkWallMapper.TryMapApiError(doc.RootElement, out var errStatus, out var code, out var message))
            {
                _logger.LogWarning("VK wall.get soft-failed: code={Code}", code);
                return new VkWallFetchResult
                {
                    Status = errStatus,
                    ErrorCode = code,
                    Message = message,
                    OwnerId = resolvedOwner,
                    ScreenName = resolvedScreen
                };
            }

            var posts = VkWallMapper.MapWallItems(doc.RootElement, out var skippedDonut);
            return new VkWallFetchResult
            {
                Status = VkFetchStatus.Ok,
                OwnerId = resolvedOwner,
                ScreenName = resolvedScreen,
                Posts = posts,
                SkippedDonutCount = skippedDonut
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "VK wall.get JSON parse failed");
            return VkWallFetchResult.Soft(
                VkFetchStatus.SoftError,
                "vk-parse",
                "VK wall response could not be parsed.");
        }
    }

    private async Task<string?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(path, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // VK often returns 200 with {"error":{...}}; still surface HTTP failures.
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning(
                    "VK API HTTP soft-failed: status={Status}",
                    (int)response.StatusCode);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Caller maps via Classify if body has error; otherwise synthesize.
                    return """{"error":{"error_code":6,"error_msg":"Too many requests per second"}}""";
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return """{"error":{"error_code":5,"error_msg":"User authorization failed"}}""";
                }

                return """{"error":{"error_code":10,"error_msg":"Internal server error"}}""";
            }

            return body;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("VK API request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "VK API transport error");
            return null;
        }
    }

    /// <summary>Strip leading @ and URL prefixes; keep alphanumeric/_/.</summary>
    public static string? NormalizeScreenName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();
        if (s.StartsWith('@'))
        {
            s = s[1..];
        }

        // Accept pasted https://vk.com/club123 or vk.com/public123
        const string httpsPrefix = "https://vk.com/";
        const string httpPrefix = "http://vk.com/";
        const string barePrefix = "vk.com/";
        if (s.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            s = s[httpsPrefix.Length..];
        }
        else if (s.StartsWith(httpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            s = s[httpPrefix.Length..];
        }
        else if (s.StartsWith(barePrefix, StringComparison.OrdinalIgnoreCase))
        {
            s = s[barePrefix.Length..];
        }

        var slash = s.IndexOf('/');
        if (slash >= 0)
        {
            s = s[..slash];
        }

        var q = s.IndexOf('?');
        if (q >= 0)
        {
            s = s[..q];
        }

        s = s.Trim();
        if (s.Length is 0 or > 64)
        {
            return null;
        }

        // Screen names / clubN / publicN / eventN — alnum + underscore.
        foreach (var ch in s)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or '.'))
            {
                return null;
            }
        }

        return s;
    }
}

/// <summary>Honest stub when VK service token is not configured. Compose stays green.</summary>
public sealed class StubVkWallClient : IVkWallClient
{
    public bool IsConfigured => false;

    public Task<VkResolveResult> ResolveScreenNameAsync(
        string screenName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(VkResolveResult.SkipNoToken());

    public Task<VkWallFetchResult> GetWallAsync(
        string? screenName = null,
        long? ownerId = null,
        int? count = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(VkWallFetchResult.SkipNoToken());
}

/// <summary>Chooses live VK client when service token present; otherwise stub skip.</summary>
public sealed class FallbackVkWallClient : IVkWallClient
{
    private readonly IVkTokenStore _tokenStore;
    private readonly HttpVkWallClient _live;
    private readonly StubVkWallClient _stub;

    public FallbackVkWallClient(
        IVkTokenStore tokenStore,
        HttpVkWallClient live,
        StubVkWallClient stub)
    {
        _tokenStore = tokenStore;
        _live = live;
        _stub = stub;
    }

    public bool IsConfigured => _tokenStore.HasToken;

    public Task<VkResolveResult> ResolveScreenNameAsync(
        string screenName,
        CancellationToken cancellationToken = default) =>
        _tokenStore.HasToken
            ? _live.ResolveScreenNameAsync(screenName, cancellationToken)
            : _stub.ResolveScreenNameAsync(screenName, cancellationToken);

    public Task<VkWallFetchResult> GetWallAsync(
        string? screenName = null,
        long? ownerId = null,
        int? count = null,
        CancellationToken cancellationToken = default) =>
        _tokenStore.HasToken
            ? _live.GetWallAsync(screenName, ownerId, count, cancellationToken)
            : _stub.GetWallAsync(screenName, ownerId, count, cancellationToken);
}
