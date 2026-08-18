using System.Net.Http.Headers;
using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Vk;

/// <summary>
/// Downloads photo bytes only from allowlisted VK CDN hosts (*.userapi.com) with size limit.
/// Does not auto-follow redirects off-allowlist.
/// </summary>
public interface IVkMediaDownloader
{
    Task<VkMediaDownloadResult> TryDownloadAsync(string mediaUrl, CancellationToken cancellationToken);
}

public sealed class VkMediaDownloadResult
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public byte[]? Bytes { get; init; }
    public string? ContentType { get; init; }
}

public sealed class VkMediaDownloader : IVkMediaDownloader
{
    private readonly HttpClient _http;
    private readonly int _maxBytes;

    public VkMediaDownloader(HttpClient http, IOptions<VkOptions> options)
    {
        _http = http;
        _maxBytes = options.Value.MaxMediaDownloadBytes;
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.RequestTimeoutSeconds, 5, 120));
    }

    public async Task<VkMediaDownloadResult> TryDownloadAsync(string mediaUrl, CancellationToken cancellationToken)
    {
        if (!VkCdnUrlGuard.IsAllowedMediaUrl(mediaUrl, out var uri, out var reason))
        {
            return new VkMediaDownloadResult
            {
                Ok = false,
                ErrorCode = reason ?? "url-rejected"
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new VkMediaDownloadResult
            {
                Ok = false,
                ErrorCode = "http-" + (int)response.StatusCode
            };
        }

        if (response.RequestMessage?.RequestUri is { } finalUri &&
            !VkCdnUrlGuard.IsAllowedMediaUrl(finalUri.ToString(), out _, out var finalReason))
        {
            return new VkMediaDownloadResult
            {
                Ok = false,
                ErrorCode = finalReason ?? "redirect-not-allowlisted"
            };
        }

        if (response.Content.Headers.ContentLength is { } len && len > _maxBytes)
        {
            return new VkMediaDownloadResult
            {
                Ok = false,
                ErrorCode = "content-too-large"
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var ms = new MemoryStream(capacity: Math.Min(_maxBytes, 64 * 1024));
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > _maxBytes)
            {
                return new VkMediaDownloadResult
                {
                    Ok = false,
                    ErrorCode = "content-too-large"
                };
            }

            ms.Write(buffer, 0, read);
        }

        return new VkMediaDownloadResult
        {
            Ok = true,
            Bytes = ms.ToArray(),
            ContentType = response.Content.Headers.ContentType?.MediaType
        };
    }
}
