using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Telemetry;
using SqlSugar;

namespace Fbs.WebApi.Auth.Clerk;

/// <summary>
/// What happens when Clerk says somebody deleted their account: they are taken out of everything, and what they made stays,
/// without them in it. Bookings are kept with who made them, but as a former member, and who they are is no longer kept.
/// </summary>
/// <remarks>
/// The point of contact written on a booking is what somebody typed for that booking, and stays with it, even if it is their own name
/// or number.
/// </remarks>
public sealed class AccountErasure(ISqlSugarClient sql, ILogger<AccountErasure> logger)
{
    public const string FormerMemberName = "Former member";

    /// <returns>Whether there was an account to erase: false if they never signed in here, or it was done already.</returns>
    public async Task<bool> EraseAsync(string clerkUserId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        List<Guid> tenantIds;

        using (var tran = sql.Ado.UseTran())
        {
            var account = await sql.Queryable<UserAccount>().Where(a => a.ClerkUserId == clerkUserId).TranLock(DbLockType.Wait).FirstAsync(ct);
            if (account is null || account.DeletedAt is not null)
            {
                return false;
            }

            var userId = account.Id;
            var places = await sql.Queryable<TenantMember>().Where(m => m.UserId == userId).TranLock(DbLockType.Wait).ToListAsync(ct);
            tenantIds = places.Select(m => m.TenantId).Distinct().ToList();

            await sql.Updateable<UserAccount>().SetColumns(a => new UserAccount { DeletedAt = now, Name = null, Email = null }).Where(a => a.Id == userId).ExecuteCommandAsync(ct);
            // Not an admin, not told anything, not found by their number, and their name is not kept, but the place stays, as bookings refer to it
            await sql.Updateable<TenantMember>()
                .SetColumns(m => new TenantMember
                {
                    DisplayName = FormerMemberName,
                    Phone = null,
                    LegacyChatId = null,
                    Role = MemberRole.Member,
                    NotificationScope = NotificationScope.None,
                    Status = MemberStatus.Removed,
                })
                .Where(m => m.UserId == userId)
                .ExecuteCommandAsync(ct);
            await sql.Deleteable<TelegramLink>().Where(l => l.UserId == userId).ExecuteCommandAsync(ct);
            await sql.Deleteable<MemberClaimToken>().Where(t => t.UserId == userId).ExecuteCommandAsync(ct);
            tran.CommitTran();
        }

        FbsMetrics.AccountsErased.Add(1);

        // An organisation whose only admin deleted their account can't be managed until somebody who runs the system makes another
        var admin = MemberRole.Admin;
        var active = MemberStatus.Active;
        foreach (var tenantId in tenantIds)
        {
            if (!await sql.Queryable<TenantMember>().AnyAsync(m => m.TenantId == tenantId && m.Role == admin && m.Status == active, ct))
            {
                logger.LogWarning("Organisation {TenantId} has no admin now that an account was deleted. Make one with promote-admin.", tenantId);
            }
        }

        return true;
    }
}
