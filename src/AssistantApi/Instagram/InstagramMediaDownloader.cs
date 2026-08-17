using System.Net.Http.Headers;
using AssistantApi.Options;
using Microsoft.Extensions.Options;

namespace AssistantApi.Instagram;

/// <summary>
/// Downloads media bytes only from allowlisted Instagram CDN hosts with size limit.
/// Does not follow redirects off-allowlist.
/// </summary>
public interface IInstagramMediaDownloader
{
    Task<InstagramMediaDownloadResult> TryDownloadAsync(string mediaUrl, CancellationToken cancellationToken);
}

public sealed class InstagramMediaDownloadResult
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public byte[]? Bytes { get; init; }
    public string? ContentType { get; init; }
}

public sealed class InstagramMediaDownloader : IInstagramMediaDownloader
{
    private readonly HttpClient _http;
    private readonly int _maxBytes;

    public InstagramMediaDownloader(HttpClient http, IOptions<InstagramOptions> options)
    {
        _http = http;
        _maxBytes = options.Value.MaxMediaDownloadBytes;
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.RequestTimeoutSeconds, 5, 120));
    }

    public async Task<InstagramMediaDownloadResult> TryDownloadAsync(string mediaUrl, CancellationToken cancellationToken)
    {
        if (!InstagramMediaUrlGuard.IsAllowedMediaUrl(mediaUrl, out var uri, out var reason))
        {
            return new InstagramMediaDownloadResult
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
            return new InstagramMediaDownloadResult
            {
                Ok = false,
                ErrorCode = "http-" + (int)response.StatusCode
            };
        }

        // Reject redirect targets that left allowlist (handler should not auto-follow off-list).
        if (response.RequestMessage?.RequestUri is { } finalUri &&
            !InstagramMediaUrlGuard.IsAllowedMediaUrl(finalUri.ToString(), out _, out var finalReason))
        {
            return new InstagramMediaDownloadResult
            {
                Ok = false,
                ErrorCode = finalReason ?? "redirect-not-allowlisted"
            };
        }

        if (response.Content.Headers.ContentLength is { } len && len > _maxBytes)
        {
            return new InstagramMediaDownloadResult
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
                return new InstagramMediaDownloadResult
                {
                    Ok = false,
                    ErrorCode = "content-too-large"
                };
            }

            ms.Write(buffer, 0, read);
        }

        return new InstagramMediaDownloadResult
        {
            Ok = true,
            Bytes = ms.ToArray(),
            ContentType = response.Content.Headers.ContentType?.MediaType
        };
    }
}
