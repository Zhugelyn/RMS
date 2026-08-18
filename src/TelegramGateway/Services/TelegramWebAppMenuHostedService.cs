using Microsoft.Extensions.Options;
using TelegramGateway.Options;

namespace TelegramGateway.Services;

/// <summary>
/// Sets MenuButtonWebApp when TELEGRAM__WEBAPPURL is configured; otherwise no-op.
/// </summary>
public sealed class TelegramWebAppMenuHostedService : IHostedService
{
    private readonly ITelegramBotClient _telegram;
    private readonly IOptions<TelegramOptions> _options;
    private readonly ILogger<TelegramWebAppMenuHostedService> _logger;

    public TelegramWebAppMenuHostedService(
        ITelegramBotClient telegram,
        IOptions<TelegramOptions> options,
        ILogger<TelegramWebAppMenuHostedService> logger)
    {
        _telegram = telegram;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var url = _options.Value.WebAppUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogInformation("TELEGRAM__WEBAPPURL unset — skip setChatMenuButton");
            return;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            _logger.LogWarning("TELEGRAM__WEBAPPURL invalid — skip setChatMenuButton");
            return;
        }

        await _telegram.SetChatMenuButtonWebAppAsync("Студия", uri.AbsoluteUri, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
