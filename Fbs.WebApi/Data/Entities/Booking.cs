using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

[SugarIndex("IX_Booking_TenantId_FacilityId_Start_End", nameof(TenantId), OrderByType.Asc, nameof(FacilityId), OrderByType.Asc, nameof(StartUtc), OrderByType.Asc, nameof(EndUtc), OrderByType.Asc)]
[SugarIndex("IX_Booking_TenantId_StartUtc", nameof(TenantId), OrderByType.Asc, nameof(StartUtc), OrderByType.Asc)]
[SugarIndex("IX_Booking_TenantId_BookedByMemberId_StartUtc", nameof(TenantId), OrderByType.Asc, nameof(BookedByMemberId), OrderByType.Asc, nameof(StartUtc), OrderByType.Asc)]
public class Booking
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid FacilityId { get; set; }

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }

    [SugarColumn(Length = 200)]
    public string Conduct { get; set; } = null!;

    [SugarColumn(Length = 2000, IsNullable = true)]
    public string? Description { get; set; }

    /// <summary>The point of contact's name, copied onto the booking when it was made. It is not looked up again.</summary>
    [SugarColumn(Length = 200, IsNullable = true)]
    public string? PocName { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? PocPhone { get; set; }

    /// <summary>Who made the booking. Never changes.</summary>
    public Guid BookedByMemberId { get; set; }

    /// <summary>Who last changed it, if anyone has.</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? UpdatedByMemberId { get; set; }

    /// <summary>The booker's unit when it was made, so it stays with the booking if they move.</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? UnitId { get; set; }

    /// <summary>Set on every booking made together, so they are told about as one.</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? BatchId { get; set; }

    /// <summary>Goes up with every change, so anything that follows the booking can tell it is out of date.</summary>
    public int Revision { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Cancelled bookings are kept, but no longer stop the facility being booked.</summary>
    [SugarColumn(IsNullable = true)]
    public DateTimeOffset? CancelledAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? CancelledByMemberId { get; set; }
}
