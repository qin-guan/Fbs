using Fbs.WebApi.Data.Entities;
using SqlSugar;

namespace Fbs.WebApi.Tenancy;

/// <summary>
/// What somebody is called in an organisation. It starts as the name on their account, and can be made something else there, by
/// them or an admin (a rank and name, say). One that is still the name on their account follows it: changing the name with Clerk
/// changes it in every organisation where it hasn't been made different.
/// </summary>
public static class MemberNames
{
    /// <summary>The longest a name in an organisation can be.</summary>
    public const int MaxLength = 200;

    /// <summary>The name an account gives somebody in an organisation: its name, or its email if it has none.</summary>
    public static string? Of(UserAccount account) =>
        (account.Name ?? account.Email) is { Length: > MaxLength } name ? name[..MaxLength] : account.Name ?? account.Email;

    /// <summary>
    /// Gives the account's places that were called what its name was (<paramref name="before"/>) the name it has now, and writes
    /// down in each organisation that they changed it. A place that has been left keeps the name it had.
    /// </summary>
    public static async Task FollowAccountAsync(ISqlSugarClient sql, UserAccount account, string? before, CancellationToken ct)
    {
        if (before is null || Of(account) is not { } after || before == after)
        {
            return;
        }

        var accountId = account.Id;
        var removed = MemberStatus.Removed;
        var places = await sql.Queryable<TenantMember>().Where(m => m.UserId == accountId && m.DisplayName == before && m.Status != removed).ToListAsync(ct);
        foreach (var place in places)
        {
            var id = place.Id;
            // Only while it is still what it was, so a request at the same moment doesn't write it down twice
            var renamed = await sql.Updateable<TenantMember>()
                .SetColumns(m => new TenantMember { DisplayName = after })
                .Where(m => m.Id == id && m.DisplayName == before)
                .ExecuteCommandAsync(ct);
            if (renamed > 0)
            {
                await new AuditLog(sql).WriteAsync(place.TenantId, id, "member.renamed", "Changed their name, with the name on their account.", "member", id, ct);
            }
        }
    }
}
