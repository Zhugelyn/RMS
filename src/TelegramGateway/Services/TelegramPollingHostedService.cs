using Microsoft.Extensions.Options;
using TelegramGateway.Options;

namespace TelegramGateway.Services;

public sealed class TelegramPollingHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<TelegramOptions> _options;
    private readonly ILogger<TelegramPollingHostedService> _logger;

    public TelegramPollingHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<TelegramOptions> options,
        ILogger<TelegramPollingHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.UsePolling)
        {
            _logger.LogInformation("Telegram polling disabled; waiting for webhook updates.");
            return;
        }

        _logger.LogInformation("Telegram long polling started.");
        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
                var processor = scope.ServiceProvider.GetRequiredService<IUpdateProcessingService>();

                var updates = await bot.GetUpdatesAsync(offset, stoppingToken);
                foreach (var update in updates)
                {
                    offset = update.UpdateId + 1;
                    await processor.ProcessAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling loop error");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
