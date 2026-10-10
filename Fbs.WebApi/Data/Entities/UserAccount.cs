using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// Someone who signs in, with the account Clerk or WorkOS keeps for them. It is made the first time they are seen, from
/// what their token says, and belongs to no tenant: they are a member of one, or several, through
/// <see cref="TenantMember"/>.
/// </summary>
/// <remarks>
/// Somebody who had a Clerk account and moved to WorkOS has both IDs, and is the same account with either, so nothing they had is
/// lost (see docs/runbooks/cutover-3-workos.md). Somebody who signed up after the move has only the WorkOS one.
/// </remarks>
[SugarIndex("UX_UserAccount_ClerkUserId", nameof(ClerkUserId), OrderByType.Asc, IsUnique = true)]
[SugarIndex("UX_UserAccount_WorkOSUserId", nameof(WorkOSUserId), OrderByType.Asc, IsUnique = true)]
public class UserAccount
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    /// <summary>Clerk's ID for them, the <c>sub</c> of their Clerk session token, if they had a Clerk account.</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ClerkUserId { get; set; }

    /// <summary>WorkOS's ID for them, the <c>sub</c> of their WorkOS access token, once they have signed in with WorkOS or been moved to it.</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? WorkOSUserId { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? Name { get; set; }

    [SugarColumn(Length = 320, IsNullable = true)]
    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When Clerk or WorkOS said the account was deleted. What they made is kept, without them in it.</summary>
    [SugarColumn(IsNullable = true)]
    public DateTimeOffset? DeletedAt { get; set; }
}
