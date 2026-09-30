using ConsoleAppFramework;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class SuspendCommand(ILogger<SuspendCommand> logger, TenantSuspensions suspensions)
{
    /// <summary>
    /// Stops an organisation being used: nobody in it can, its admins included, and nobody can join it with a link. Nothing is
    /// deleted, and <c>unsuspend</c> undoes it. Exits with 1 if there is no such organisation.
    /// </summary>
    /// <param name="tenant">The slug of the organisation.</param>
    /// <param name="cancellationToken"></param>
    [Command("suspend")]
    public Task<int> Suspend(string tenant, CancellationToken cancellationToken = default) => Set(tenant, true, cancellationToken);

    /// <summary>Lets an organisation that was suspended be used again. Exits with 1 if there is no such organisation.</summary>
    /// <param name="tenant">The slug of the organisation.</param>
    /// <param name="cancellationToken"></param>
    [Command("unsuspend")]
    public Task<int> Unsuspend(string tenant, CancellationToken cancellationToken = default) => Set(tenant, false, cancellationToken);

    private async Task<int> Set(string tenant, bool suspended, CancellationToken cancellationToken)
    {
        var outcome = await suspensions.SetAsync(tenant, suspended, cancellationToken);
        switch (outcome)
        {
            case SuspensionOutcome.Changed:
                logger.LogInformation("{Tenant} is {State}.", tenant, suspended ? "suspended" : "active again");
                return 0;
            case SuspensionOutcome.AlreadyThatWay:
                logger.LogInformation("{Tenant} was {State} already.", tenant, suspended ? "suspended" : "active");
                return 0;
            case SuspensionOutcome.PendingDeletion:
                logger.LogError("{Tenant} is scheduled for deletion, so it can't be suspended or made active: its admins can restore it, and purge-tenants deletes it.", tenant);
                return 1;
            default:
                logger.LogError("There is no organisation {Tenant}.", tenant);
                return 1;
        }
    }
}
