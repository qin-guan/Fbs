using ConsoleAppFramework;
using Fbs.WebApi.Legacy;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class ImportLegacyCommand(ILogger<ImportLegacyCommand> logger, LegacyImporter importer)
{
    /// <summary>
    /// Copies the users, facilities, nominal roll and bookings from Google Sheets and Calendar into the database.
    /// Needs the same <c>Google__*</c> settings as the API. Everything is done in one transaction, so it is all
    /// imported or none of it.
    /// </summary>
    /// <param name="tenant">The slug of the tenant they go to, which is made if it isn't there.</param>
    /// <param name="name">What a tenant that is made is called. Defaults to the slug.</param>
    /// <param name="dryRun">Do everything, and say what happened, but don't keep any of it.</param>
    /// <param name="overwrite">Replace what an earlier import saved with what is in Google now, for the last import before switching over. Refused once bookings have been made in the database.</param>
    /// <param name="cancellationToken"></param>
    [Command("import-legacy")]
    public async Task<int> Import(
        string tenant = "3sib",
        string? name = null,
        bool dryRun = false,
        bool overwrite = false,
        CancellationToken cancellationToken = default
    )
    {
        var report = await importer.ImportAsync(
            new LegacyImportOptions
            {
                Slug = tenant,
                Name = name,
                DryRun = dryRun,
                Overwrite = overwrite,
            },
            cancellationToken
        );

        foreach (var line in report.Summary())
        {
            if (line.StartsWith("Warning:", StringComparison.Ordinal))
            {
                logger.LogWarning("{Line}", line);
            }
            else
            {
                logger.LogInformation("{Line}", line);
            }
        }

        return 0;
    }
}
