using Microsoft.Extensions.Diagnostics.HealthChecks;
using SqlSugar;

namespace Fbs.WebApi.Health;

/// <summary>
/// Whether the database answers. A new version that can't reach it isn't ready to take over from the old one,
/// which is what the hosting platform's check on <c>/health</c> is for. It says nothing about why, as that
/// endpoint is open.
/// </summary>
public sealed class DatabaseHealthCheck(ISqlSugarClient sql, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await sql.Ado.GetIntAsync("SELECT 1").WaitAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(e, "The database did not answer");
            return HealthCheckResult.Unhealthy("The database did not answer.");
        }
    }
}
