using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// A one-time token in the Telegram link a signed-in user opens to claim their imported member (see
/// <see cref="Fbs.WebApi.Claims.MemberClaims"/>). Valid once, for 10 minutes; only its SHA-256 hash is stored.
/// </summary>
[SugarIndex("UX_MemberClaimToken_TokenHash", nameof(TokenHash), OrderByType.Asc, IsUnique = true)]
[SugarIndex("IX_MemberClaimToken_UserId_TenantId", nameof(UserId), OrderByType.Asc, nameof(TenantId), OrderByType.Asc)]
public class MemberClaimToken
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    /// <summary>The account that asked for the link, and that the claimed member is attached to.</summary>
    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }

    [SugarColumn(Length = 64)]
    public string TokenHash { get; set; } = null!;

    [SugarColumn(ColumnDataType = "datetime(6)")]
    public DateTimeOffset ExpiresAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTimeOffset? UsedAt { get; set; }
}
