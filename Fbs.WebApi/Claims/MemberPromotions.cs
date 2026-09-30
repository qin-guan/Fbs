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

    /// <summary>They haven't signed in and taken their place yet, or aren't in the organisation any more.</summary>
    NotActive = 5,
}

/// <summary>
/// Makes somebody an admin. This is for whoever runs the system: people carried over from before become members when they
/// claim their places, so that an admin is always somebody who was chosen, and this is how the first one is.
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
        // As it would be typed in the organisation's country, and as the old version wrote it, with the calling code and no plus
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        var candidates = new[] { MemberPhones.Normalize(phone, tenant.DefaultCountryCode), digits.Length == 0 ? null : "+" + digits }.Where(p => p is not null).Distinct().ToList();
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
