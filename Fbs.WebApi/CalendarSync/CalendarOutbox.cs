using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using SqlSugar;

namespace Fbs.WebApi.CalendarSync;

/// <summary>What the outbox message that says a booking's event in the calendar is out of date holds.</summary>
public sealed class CalendarBookingPayload
{
    public Guid BookingId { get; set; }

    /// <summary>Send it even though what was sent last is the version it is now, to put back what was changed in the calendar.</summary>
    public bool Force { get; set; }
}

public static class CalendarOutbox
{
    public const string MessageType = "calendar.booking";

    /// <summary>
    /// Says that the bookings' events in the tenant's calendar are out of date, if it has one that is
    /// working. Call it in the same transaction as the change. It doesn't say what changed: what is sent is
    /// what the booking is when it is sent, so messages that are retried, or overtaken, can't send old news.
    /// </summary>
    /// <returns>Whether there was a calendar to send them to.</returns>
    public static async Task<bool> EnqueueAsync(
        ISqlSugarClient sql,
        Guid tenantId,
        IEnumerable<Guid> bookingIds,
        CancellationToken cancellationToken = default
    )
    {
        var active = CalendarConnectionStatus.Active;
        if (!await sql.Queryable<CalendarConnection>().AnyAsync(c => c.TenantId == tenantId && c.Status == active, cancellationToken))
        {
            return false;
        }

        foreach (var bookingId in bookingIds)
        {
            await OutboxWriter.EnqueueAsync(
                sql,
                tenantId,
                MessageType,
                new CalendarBookingPayload { BookingId = bookingId },
                cancellationToken
            );
        }

        return true;
    }
}
