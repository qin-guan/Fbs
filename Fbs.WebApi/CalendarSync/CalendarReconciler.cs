using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Fbs.WebApi.CalendarSync;

public sealed class CalendarSyncOptions
{
    /// <summary>How long after starting it first checks, so a deploy isn't also a burst of calendar traffic.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How often it checks that every booking is in the calendar as it is now.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Now and then checks that each tenant's calendar has every booking as it is now, and sends those that it
/// doesn't, such as when Google was down for longer than the outbox keeps trying.
/// </summary>
public sealed class CalendarReconciler(
    ILogger<CalendarReconciler> logger,
    ISqlSugarClient sql,
    OutboxSignal signal,
    IOptions<OutboxOptions> outbox,
    IOptions<CalendarSyncOptions> options
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(options.Value.InitialDelay, stoppingToken);
            using var timer = new PeriodicTimer(options.Value.Interval);
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(e, "Checking the calendars failed, and will be tried again");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <returns>How many bookings were found to be out of date.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var active = CalendarConnectionStatus.Active;
        var onlyTenant = outbox.Value.TenantId;
        var tenantIds = await sql.Queryable<CalendarConnection>()
            .Where(c => c.Status == active)
            .WhereIF(onlyTenant is not null, c => c.TenantId == onlyTenant)
            .Select(c => c.TenantId)
            .ToListAsync(cancellationToken);

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            var count = await CalendarSyncPlanner.EnqueueOutOfDateAsync(sql, signal, tenantId, cancellationToken: cancellationToken);
            if (count > 0)
            {
                logger.LogInformation("Sending {Count} bookings to tenant {TenantId}'s calendar that were out of date", count, tenantId);
            }

            total += count;
        }

        return total;
    }
}
