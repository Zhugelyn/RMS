using Microsoft.Extensions.Options;

namespace AssistantApi.Research;

public sealed class ResearchSchedulerOptions
{
    public const string SectionName = "ResearchScheduler";

    /// <summary>Poll interval for due research_settings. Default 60s.</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// BackgroundService: enabled research_settings with nextRunAt≤now → capture job.
/// No Hangfire. Tick failures never kill the host process.
/// </summary>
public sealed class ResearchSchedulerHostedService : BackgroundService
{
    private readonly IResearchSchedulerJob _job;
    private readonly IOptions<ResearchSchedulerOptions> _options;
    private readonly ILogger<ResearchSchedulerHostedService> _logger;

    public ResearchSchedulerHostedService(
        IResearchSchedulerJob job,
        IOptions<ResearchSchedulerOptions> options,
        ILogger<ResearchSchedulerHostedService> logger)
    {
        _job = job;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Research scheduler hosted service started");
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _options.Value;
            if (opts.Enabled)
            {
                try
                {
                    await _job.TickAsync(DateTimeOffset.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Hard guarantee: Graph/settings failures must not tear down the host.
                    _logger.LogWarning(ex, "Research scheduler tick crashed; continuing");
                }
            }

            var delay = TimeSpan.FromSeconds(Math.Clamp(opts.PollIntervalSeconds, 5, 3600));
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Research scheduler hosted service stopped");
    }
}
