using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// Which version of a booking its event in the calendar shows, so a booking that is already up to date is
/// not sent again, and one that was cancelled can have its event removed.
/// </summary>
public class BookingCalendarEvent
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid BookingId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Which calendar it is in, so changing the tenant's calendar sends everything again.</summary>
    [SugarColumn(Length = 256)]
    public string CalendarId { get; set; } = null!;

    /// <summary>What Google calls the event: the booking's ID without dashes, the same as before bookings were in the database.</summary>
    [SugarColumn(Length = 64)]
    public string EventId { get; set; } = null!;

    /// <summary>The <see cref="Booking.Revision"/> the event shows.</summary>
    public int SyncedRevision { get; set; }

    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
