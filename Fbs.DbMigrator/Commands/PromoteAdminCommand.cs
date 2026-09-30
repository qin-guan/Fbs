using ConsoleAppFramework;
using Fbs.WebApi.Claims;
using Microsoft.Extensions.Logging;

namespace Fbs.DbMigrator.Commands;

public class PromoteAdminCommand(ILogger<PromoteAdminCommand> logger, MemberPromotions promotions)
{
    /// <summary>
    /// Makes a member an admin of an organisation. People carried over from the old version become members when they claim
    /// their places, so this is how the first admin of such an organisation is made, and it can be used for any other
    /// too. They have to have signed in and taken their place first. Exits with 1 if it couldn't be done.
    /// </summary>
    /// <param name="tenant">The slug of the organisation.</param>
    /// <param name="phone">The member's phone number, in the form it is stored in or as it would be typed in the organisation's country.</param>
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
                logger.LogError("{Phone} hasn't signed in and taken their place in {Tenant} yet, or has left it.", phone, tenant);
                return 1;
        }
    }
}
