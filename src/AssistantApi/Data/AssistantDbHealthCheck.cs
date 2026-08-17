using AssistantApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AssistantApi.Data;

/// <summary>Ready fails when ConnectionStrings:AssistantDb is set but Postgres is unreachable.</summary>
public sealed class AssistantDbHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;

    public AssistantDbHealthCheck(IDbContextFactory<AssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("AssistantDb reachable")
                : HealthCheckResult.Unhealthy("AssistantDb unreachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("AssistantDb check failed", ex);
        }
    }
}
