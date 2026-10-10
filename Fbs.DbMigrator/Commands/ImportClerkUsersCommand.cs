using ConsoleAppFramework;
using Fbs.WebApi.Auth.WorkOS;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class ImportClerkUsersCommand(ILogger<ImportClerkUsersCommand> logger, ClerkUserImporter importer)
{
    /// <summary>
    /// Moves everybody from Clerk to WorkOS, with their passwords, and joins each one's account here to their WorkOS user, so
    /// nobody has to do anything but sign in (docs/runbooks/cutover-3-workos.md). Needs WorkOS__ApiKey. It can be run again, and is,
    /// just before the switch. Exits with 1 if anybody couldn't be moved or joined, and says who, by their Clerk user ID.
    /// </summary>
    /// <param name="export">The CSV of users exported from Clerk's dashboard, with their password hashes.</param>
    /// <param name="dryRun">Read WorkOS and the database, and say what would be done, but change nothing in either.</param>
    /// <param name="cancellationToken"></param>
    [Command("import-clerk-users")]
    public async Task<int> Import(string export, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClerkExportedUser> users;
        try
        {
            users = await ClerkExport.ReadCsvAsync(export, cancellationToken);
        }
        catch (Exception e) when (e is FormatException or IOException)
        {
            logger.LogError("Couldn't read the export: {Reason}", e.Message);
            return 1;
        }

        var report = await importer.ImportAsync(users, dryRun, cancellationToken);
        foreach (var line in report.Summary())
        {
            if (line.StartsWith("Error:", StringComparison.Ordinal))
            {
                logger.LogError("{Line}", line);
            }
            else if (line.StartsWith("Warning:", StringComparison.Ordinal))
            {
                logger.LogWarning("{Line}", line);
            }
            else
            {
                logger.LogInformation("{Line}", line);
            }
        }

        return report.Errors.Count > 0 ? 1 : 0;
    }
}
