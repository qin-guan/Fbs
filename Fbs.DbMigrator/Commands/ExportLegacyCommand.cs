using ConsoleAppFramework;
using Fbs.WebApi.Legacy;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class ExportLegacyCommand(ILogger<ExportLegacyCommand> logger, LegacyExporter exporter)
{
    /// <summary>
    /// Writes bookings made, changed and cancelled in the database back to Google Calendar, in the form the old
    /// version reads, so that going back to it after the switch loses nothing. Needs the same Google__* settings
    /// as the API. Exits with 2 when something could not be written.
    /// </summary>
    /// <param name="tenant">The slug of the tenant whose bookings they are.</param>
    /// <param name="dryRun">Say what would be written, without writing it.</param>
    /// <param name="cancellationToken"></param>
    [Command("export-legacy")]
    public async Task<int> Export(string tenant = "3sib", bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var report = await exporter.ExportAsync(tenant, dryRun, cancellationToken);

        foreach (var line in report.Summary())
        {
            if (line.StartsWith("Failed:", StringComparison.Ordinal))
            {
                logger.LogWarning("{Line}", line);
            }
            else
            {
                logger.LogInformation("{Line}", line);
            }
        }

        return report.Failures.Count == 0 ? 0 : 2;
    }
}
