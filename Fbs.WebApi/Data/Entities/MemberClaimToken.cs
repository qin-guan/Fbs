using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// What lets somebody who is signed in take over the place a member was carried over with from before, once they
/// show they are who that place is for by opening a link in the Telegram chat it was linked to. It works once, for
/// a few minutes, and only a hash of it is kept.
/// </summary>
[SugarIndex("UX_MemberClaimToken_TokenHash", nameof(TokenHash), OrderByType.Asc, IsUnique = true)]
[SugarIndex("IX_MemberClaimToken_UserId_TenantId", nameof(UserId), OrderByType.Asc, nameof(TenantId), OrderByType.Asc)]
public class MemberClaimToken
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    /// <summary>The account that asked for it, which is who gets the place.</summary>
    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }

    [SugarColumn(Length = 64)]
    public string TokenHash { get; set; } = null!;

    [SugarColumn(ColumnDataType = "datetime(6)")]
    public DateTimeOffset ExpiresAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTimeOffset? UsedAt { get; set; }
}
