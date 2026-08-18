using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RagService.Options;

namespace RagService.Security;

public sealed class ServiceKeyAuthMiddleware
{
    public const string HeaderName = "X-Service-Key";

    private readonly RequestDelegate _next;
    private readonly byte[] _expectedKeyBytes;

    public ServiceKeyAuthMiddleware(RequestDelegate next, IOptions<ServiceAuthOptions> options)
    {
        _next = next;
        var key = options.Value.ServiceKey;
        _expectedKeyBytes = Encoding.UTF8.GetBytes(key);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided) ||
            string.IsNullOrWhiteSpace(provided))
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        if (providedBytes.Length != _expectedKeyBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(providedBytes, _expectedKeyBytes))
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        await _next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            title = "Unauthorized",
            status = 401,
            detail = "Valid X-Service-Key required."
        });
    }
}
