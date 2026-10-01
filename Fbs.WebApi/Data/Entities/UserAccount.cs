using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// Someone who signs in, with the account Clerk keeps for them. It is made the first time they are seen, from
/// what their session token says, and belongs to no tenant: they are a member of one, or several, through
/// <see cref="TenantMember"/>.
/// </summary>
[SugarIndex("UX_UserAccount_ClerkUserId", nameof(ClerkUserId), OrderByType.Asc, IsUnique = true)]
public class UserAccount
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    /// <summary>Clerk's ID for them, the <c>sub</c> of their session token.</summary>
    [SugarColumn(Length = 64)]
    public string ClerkUserId { get; set; } = null!;

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? Name { get; set; }

    [SugarColumn(Length = 320, IsNullable = true)]
    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When Clerk said the account was deleted. What they made is kept, without them in it.</summary>
    [SugarColumn(IsNullable = true)]
    public DateTimeOffset? DeletedAt { get; set; }
}
