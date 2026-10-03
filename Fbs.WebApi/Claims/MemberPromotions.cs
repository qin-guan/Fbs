using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Claims;

public enum PromotionOutcome
{
    Promoted = 1,
    AlreadyAdmin = 2,
    NoSuchOrganization = 3,
    NoSuchMember = 4,

    /// <summary>The member hasn't claimed yet (still unclaimed), is waiting for approval, or was removed.</summary>
    NotActive = 5,
}

/// <summary>
/// Makes an active member an admin, for whoever runs the system (the <c>promote-admin</c> command). A claim always gives the role
/// Member (see <see cref="MemberClaims"/>), and only admins can promote in the app, so this is how an imported organisation
/// gets its first admins back.
/// </summary>
public sealed class MemberPromotions(ISqlSugarClient sql)
{
    public async Task<PromotionOutcome> PromoteAsync(string tenantSlug, string phone, CancellationToken ct)
    {
        var tenant = await sql.Queryable<Tenant>().FirstAsync(t => t.Slug == tenantSlug, ct);
        if (tenant is null)
        {
            return PromotionOutcome.NoSuchOrganization;
        }

        var tenantId = tenant.Id;
        // Without a "+", a number could be local ("9123 4567") or already have the calling code, as the old sheet wrote it
        // ("6591234567"). Look for both; if they match two different members, it is not clear who was meant.
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        var asTyped = MemberPhones.Normalize(phone, tenant.DefaultCountryCode);
        var asInSheet = digits.Length == 0 ? null : "+" + digits;
        var candidates = new[] { asTyped, asInSheet }.Where(p => p is not null).Distinct().ToList();
        var matches = candidates.Count == 0 ? [] : await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId && candidates.Contains(m.Phone)).ToListAsync(ct);
        var member = matches.Count == 1 ? matches[0] : null;
        if (member is null)
        {
            return PromotionOutcome.NoSuchMember;
        }

        if (member.Status != MemberStatus.Active)
        {
            return PromotionOutcome.NotActive;
        }

        if (member.Role == MemberRole.Admin)
        {
            return PromotionOutcome.AlreadyAdmin;
        }

        var id = member.Id;
        await sql.Updateable<TenantMember>().SetColumns(m => new TenantMember { Role = MemberRole.Admin }).Where(m => m.Id == id).ExecuteCommandAsync(ct);
        return PromotionOutcome.Promoted;
    }
}
