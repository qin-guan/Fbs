using Fbs.WebApi.Data;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Legacy;

/// <summary>Runs <see cref="SheetsReferenceSync"/> now and then, once <c>ReferenceData:Sheets:Enabled</c> says to.</summary>
public sealed class SheetsReferenceSyncService(
    ILogger<SheetsReferenceSyncService> logger,
    IServiceScopeFactory scopeFactory,
    IOptions<SheetsSyncOptions> options
) : BackgroundService
{
    /// <summary>What it last warned about, so a sheet that stays wrong is said so once and not every few minutes.</summary>
    private string _lastWarnings = string.Empty;

    // Its own client. The sync is resolved from a scope, but the client is the singleton, keyed by this context.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        SqlSugarContext.RunIsolatedAsync(() => SyncLoopAsync(stoppingToken));

    private async Task SyncLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(options.Value.InitialDelay, stoppingToken);
            using var timer = new PeriodicTimer(options.Value.Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>One look at the sheets. Whatever goes wrong, such as Google being down, it is tried again next time.</summary>
    public async Task<SheetsReferenceSyncReport?> RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var report = await scope.ServiceProvider.GetRequiredService<SheetsReferenceSync>().SyncAsync(cancellationToken);

            var warnings = string.Join('\n', report.Warnings);
            if (warnings != _lastWarnings)
            {
                _lastWarnings = warnings;
                foreach (var warning in report.Warnings)
                {
                    logger.LogWarning("Sheets: {Warning}", warning);
                }
            }

            if (report.Changed)
            {
                logger.LogInformation(
                    "Kept in step with the sheets: members {Members} (removed {MembersRemoved}, restored {MembersRestored}), facilities {Facilities} (closed {FacilitiesClosed}), roster {Roster} (removed {RosterRemoved}), units {Units}",
                    report.Members,
                    report.MembersRemoved,
                    report.MembersRestored,
                    report.Facilities,
                    report.FacilitiesClosed,
                    report.Roster,
                    report.RosterRemoved,
                    report.Units
                );
            }

            return report;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(e, "Keeping in step with the sheets failed, and will be tried again");
            return null;
        }
    }
}
