using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// A link an admin shares for people to join with, in a chat, as there is no email. Only a hash of the token is kept,
/// so what is stored can't be used to join.
/// </summary>
[SugarIndex("UX_TenantInvite_TokenHash", nameof(TokenHash), OrderByType.Asc, IsUnique = true)]
[SugarIndex("IX_TenantInvite_TenantId_CreatedAt", nameof(TenantId), OrderByType.Asc, nameof(CreatedAt), OrderByType.Desc)]
public class TenantInvite
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>SHA-256 of the token, in lower case hex.</summary>
    [SugarColumn(Length = 64)]
    public string TokenHash { get; set; } = null!;

    /// <summary>What whoever joins with it is.</summary>
    public MemberRole Role { get; set; } = MemberRole.Member;

    /// <summary>The unit whoever joins with it is put in, if any.</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? UnitId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int MaxUses { get; set; }

    public int Uses { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The admin who made it.</summary>
    public Guid CreatedByMemberId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
