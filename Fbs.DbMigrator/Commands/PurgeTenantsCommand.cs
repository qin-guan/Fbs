using ConsoleAppFramework;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class PurgeTenantsCommand(ILogger<PurgeTenantsCommand> logger, TenantPurges purges)
{
    /// <summary>
    /// Deletes for good the organisations that an admin asked to be deleted, and whose time to change their minds has passed,
    /// and everything of them: bookings, people, facilities, links and history. Accounts stay. Run it on a schedule. Exits with 1
    /// if <c>--tenant</c> is given and that one can't be deleted (there is no such organisation, it isn't to be deleted, or it isn't time).
    /// </summary>
    /// <param name="tenant">Only this one, by its slug.</param>
    /// <param name="early">Delete it before the time is up. Only for one that an admin has asked to be deleted, and for when somebody has to be erased sooner.</param>
    /// <param name="dryRun">Say what would be deleted, and delete nothing.</param>
    /// <param name="cancellationToken"></param>
    [Command("purge-tenants")]
    public async Task<int> Purge(string? tenant = null, bool early = false, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (tenant is null && early)
        {
            logger.LogError("--early is for one organisation, which is named with --tenant.");
            return 1;
        }

        var slugs = tenant is null ? await purges.DueAsync(now, cancellationToken) : [tenant];
        if (slugs.Count == 0)
        {
            logger.LogInformation("No organisation is due to be deleted.");
        }

        foreach (var slug in slugs)
        {
            var result = await purges.PurgeAsync(slug, now, early, dryRun, cancellationToken);
            switch (result.Outcome)
            {
                case PurgeOutcome.Purged:
                    logger.LogInformation("{Verb} {Slug}: {Deleted}.", dryRun ? "Would delete" : "Deleted", slug, string.Join(", ", result.Deleted.Where(d => d.Value > 0).Select(d => $"{d.Value} {d.Key}")));
                    break;
                case PurgeOutcome.NotFound:
                    logger.LogError("There is no organisation {Tenant}.", slug);
                    return 1;
                default:
                    if (tenant is not null)
                    {
                        logger.LogError("{Tenant} isn't to be deleted, or it isn't time yet. An admin asks for it to be deleted, and it can be from the day given in its deletion.", slug);
                        return 1;
                    }

                    break;
            }
        }

        return 0;
    }
}
