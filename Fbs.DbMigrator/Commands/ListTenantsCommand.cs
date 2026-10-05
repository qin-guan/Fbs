using ConsoleAppFramework;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class ListTenantsCommand(ILogger<ListTenantsCommand> logger, TenantSuspensions suspensions)
{
    /// <summary>Lists the organisations, the newest first, with their status, how many people they have and how many bookings they made in the last day.</summary>
    /// <param name="cancellationToken"></param>
    [Command("list-tenants")]
    public async Task<int> List(CancellationToken cancellationToken = default)
    {
        foreach (var tenant in await suspensions.ListAsync(cancellationToken))
        {
            logger.LogInformation("{Slug} ({Name}): {Status}, {People} people, {Bookings} bookings in the last day, made {CreatedAt:u}", tenant.Slug, tenant.Name, tenant.Status, tenant.People, tenant.BookingsInLastDay, tenant.CreatedAt);
        }

        return 0;
    }
}
