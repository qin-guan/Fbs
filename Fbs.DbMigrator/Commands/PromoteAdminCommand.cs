using ConsoleAppFramework;
using Fbs.WebApi.Claims;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class PromoteAdminCommand(ILogger<PromoteAdminCommand> logger, MemberPromotions promotions)
{
    /// <summary>
    /// Makes a member an admin of an organisation. Claiming never gives admin rights, so after Cutover 2 this is how the old
    /// admins get them back (docs/runbooks/cutover-2-accounts.md). The member must have claimed first. Exits with 1 if it
    /// couldn't be done.
    /// </summary>
    /// <param name="tenant">The slug of the organisation.</param>
    /// <param name="phone">The member's phone number: +6591234567, 6591234567 (as in the old sheet) or 9123 4567 (local).</param>
    /// <param name="cancellationToken"></param>
    [Command("promote-admin")]
    public async Task<int> Promote(string tenant, string phone, CancellationToken cancellationToken = default)
    {
        var outcome = await promotions.PromoteAsync(tenant, phone, cancellationToken);
        switch (outcome)
        {
            case PromotionOutcome.Promoted:
                logger.LogInformation("{Phone} is now an admin of {Tenant}.", phone, tenant);
                return 0;
            case PromotionOutcome.AlreadyAdmin:
                logger.LogInformation("{Phone} is an admin of {Tenant} already.", phone, tenant);
                return 0;
            case PromotionOutcome.NoSuchOrganization:
                logger.LogError("There is no organisation {Tenant}.", tenant);
                return 1;
            case PromotionOutcome.NoSuchMember:
                logger.LogError("There is nobody in {Tenant} with the number {Phone}.", tenant, phone);
                return 1;
            default:
                logger.LogError("{Phone} in {Tenant} hasn't claimed yet, is waiting for approval, or was removed.", phone, tenant);
                return 1;
        }
    }
}
