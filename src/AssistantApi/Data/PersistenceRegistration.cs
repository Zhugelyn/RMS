using AssistantApi.Data;
using AssistantApi.Memory;
using AssistantApi.Research;
using Microsoft.EntityFrameworkCore;

namespace AssistantApi.Data;

public static class PersistenceRegistration
{
    public const string ConnectionStringName = "AssistantDb";

    public static string? ResolveConnectionString(IConfiguration configuration)
    {
        var fromCs = configuration.GetConnectionString(ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(fromCs))
        {
            return fromCs.Trim();
        }

        // Alias used in .env.example for humans; maps to ConnectionStrings:AssistantDb in compose.
        var alias = configuration["POSTGRES:CONNECTIONSTRING"]
                    ?? configuration["POSTGRES__CONNECTIONSTRING"];
        return string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
    }

    public static bool HasPostgres(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(ResolveConnectionString(configuration));

    public static IServiceCollection AddAssistantPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IHarnessMemoryStore, InMemoryHarnessMemoryStore>();
            services.AddSingleton<IResearchSettingsStore, InMemoryResearchSettingsStore>();
            services.AddSingleton<IResearchArtifactStore, InMemoryResearchArtifactStore>();
            return services;
        }

        services.AddDbContextFactory<AssistantDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSingleton<IHarnessMemoryStore, PostgresHarnessMemoryStore>();
        services.AddSingleton<IResearchSettingsStore, PostgresResearchSettingsStore>();
        services.AddSingleton<IResearchArtifactStore, PostgresResearchArtifactStore>();
        services.AddSingleton<AssistantDbHealthCheck>();
        services.AddHostedService<MigrateAssistantDbHostedService>();
        return services;
    }
}

/// <summary>Applies EF migrations at startup when ConnectionStrings:AssistantDb is set.</summary>
public sealed class MigrateAssistantDbHostedService : IHostedService
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;
    private readonly ILogger<MigrateAssistantDbHostedService> _logger;

    public MigrateAssistantDbHostedService(
        IDbContextFactory<AssistantDbContext> dbFactory,
        ILogger<MigrateAssistantDbHostedService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        _logger.LogInformation("Applying AssistantDb EF migrations");
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
