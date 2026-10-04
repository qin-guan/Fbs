using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// An organisation that uses the system, such as a unit. Everything else belongs to one.
/// </summary>
[SugarIndex("UX_Tenant_Slug", nameof(Slug), OrderByType.Asc, IsUnique = true)]
[SugarIndex("IX_Tenant_CreatedByUserId", nameof(CreatedByUserId), OrderByType.Asc)]
public class Tenant
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    /// <summary>What identifies the tenant in URLs. Lower case letters, digits and hyphens.</summary>
    [SugarColumn(Length = 63)]
    public string Slug { get; set; } = null!;

    [SugarColumn(Length = 200)]
    public string Name { get; set; } = null!;

    /// <summary>An IANA time zone. Times are stored in UTC and shown, and validated, in this one.</summary>
    [SugarColumn(Length = 64)]
    public string TimeZone { get; set; } = "Asia/Singapore";

    /// <summary>The calling code that phone numbers without one are taken to be in, without the plus.</summary>
    [SugarColumn(Length = 4)]
    public string DefaultCountryCode { get; set; } = "65";

    /// <summary>The length of the smallest bookable slot. Bookings start and end on these.</summary>
    public int SlotMinutes { get; set; } = 30;

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>
    /// Whether someone who joins with an invite waits for an admin to let them in. On unless the tenant chooses
    /// otherwise, so a link that gets shared further than it should exposes nothing.
    /// </summary>
    public bool RequireApproval { get; set; } = true;

    /// <summary>
    /// Whether members imported from the old version can still claim their rows with a Clerk account (see
    /// <see cref="Fbs.WebApi.Claims.MemberClaims"/>). The importer turns it on; admins turn it off about three months after
    /// Cutover 2. It can never be turned back on from the app.
    /// </summary>
    public bool LegacyClaimEnabled { get; set; }

    /// <summary>Who made it, if it was made by someone signing up rather than carried over from before.</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum TenantStatus
{
    Active = 1,
    Suspended = 2,
}
