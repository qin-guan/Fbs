using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// Someone who belongs to a tenant, and the one bookings are made by. Until they sign in with an
/// account of their own <see cref="UserId"/> is empty, which is how existing members are carried over.
/// </summary>
[SugarIndex("UX_TenantMember_TenantId_Phone", nameof(TenantId), OrderByType.Asc, nameof(Phone), OrderByType.Asc, IsUnique = true)]
[SugarIndex("UX_TenantMember_TenantId_UserId", nameof(TenantId), OrderByType.Asc, nameof(UserId), OrderByType.Asc, IsUnique = true)]
[SugarIndex("IX_TenantMember_UserId", nameof(UserId), OrderByType.Asc)]
public class TenantMember
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>The account they sign in with, once there is one.</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? UserId { get; set; }

    [SugarColumn(Length = 200)]
    public string DisplayName { get; set; } = null!;

    /// <summary>In E.164 form, such as +6591234567. Members can be reached, and until they have an account found, by it.</summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Phone { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? UnitId { get; set; }

    public MemberRole Role { get; set; } = MemberRole.Member;

    /// <summary>Whose bookings they hear about on Telegram.</summary>
    public NotificationScope NotificationScope { get; set; } = NotificationScope.None;

    public MemberStatus Status { get; set; } = MemberStatus.Active;

    /// <summary>The Telegram chat linked to the phone number, from before members had accounts.</summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? LegacyChatId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum MemberRole
{
    Member = 1,
    Admin = 2,
}

public enum NotificationScope
{
    None = 0,
    Unit = 1,
    All = 2,
}

public enum MemberStatus
{
    Active = 1,
    Pending = 2,
    Unclaimed = 3,
    Removed = 4,
}
