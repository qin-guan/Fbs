using ConsoleAppFramework;
using Fbs.WebApi.Legacy;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class VerifyLegacyCommand(ILogger<VerifyLegacyCommand> logger, LegacyVerifier verifier)
{
    /// <summary>
    /// Checks that the database has everything that is in Google Sheets and Calendar, as it is. Exits with 2 when
    /// it doesn't, so a pipeline can tell. Only means something before switching over.
    /// </summary>
    /// <param name="tenant">The slug of the tenant to check.</param>
    /// <param name="cancellationToken"></param>
    [Command("verify-legacy")]
    public async Task<int> Verify(string tenant = "3sib", CancellationToken cancellationToken = default)
    {
        var report = await verifier.VerifyAsync(tenant, cancellationToken);

        foreach (var line in report.Summary())
        {
            if (line.StartsWith("Different:", StringComparison.Ordinal))
            {
                logger.LogWarning("{Line}", line);
            }
            else
            {
                logger.LogInformation("{Line}", line);
            }
        }

        return report.Matches ? 0 : 2;
    }
}
