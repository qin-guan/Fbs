using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using SqlSugar;

namespace Fbs.WebApi.CalendarSync;

/// <summary>
/// Works out which bookings' events in the calendar are out of date, from what was sent, and says so in
/// the outbox. It is how a calendar that has just been connected gets every booking, how one that was put
/// right after failing catches up, and how changes made in the calendar by hand are put back.
/// </summary>
public static class CalendarSyncPlanner
{
    /// <summary>
    /// Bookings that haven't ended yet, or ended within this long ago, are kept in step. Older ones are
    /// history that nobody looks at in the calendar.
    /// </summary>
    public static readonly TimeSpan HistoryKept = TimeSpan.FromDays(7);

    /// <param name="all">Every booking, even those that show the version they are now, to put back what was changed by hand.</param>
    /// <returns>How many were found to be out of date, or 0 if the tenant has no calendar that is working.</returns>
    public static async Task<int> EnqueueOutOfDateAsync(
        ISqlSugarClient sql,
        OutboxSignal signal,
        Guid tenantId,
        bool all = false,
        CancellationToken cancellationToken = default
    )
    {
        var active = CalendarConnectionStatus.Active;
        var connection = await sql.Queryable<CalendarConnection>().FirstAsync(c => c.TenantId == tenantId && c.Status == active, cancellationToken);
        if (connection is null)
        {
            return 0;
        }

        var since = DateTimeOffset.UtcNow - HistoryKept;
        var calendarId = connection.CalendarId;
        var bookings = await sql.Queryable<Booking>()
            .Where(b => b.TenantId == tenantId && b.CancelledAt == null && b.EndUtc > since)
            .Select(b => new { b.Id, b.Revision })
            .ToListAsync(cancellationToken);
        var sent = (await sql.Queryable<BookingCalendarEvent>().Where(s => s.TenantId == tenantId && s.CalendarId == calendarId).ToListAsync(cancellationToken))
            .ToDictionary(s => s.BookingId, s => s.SyncedRevision);

        var outOfDate = bookings
            .Where(b => all || !sent.TryGetValue(b.Id, out var revision) || revision < b.Revision)
            .Select(b => b.Id)
            .ToList();
        var forced = all ? outOfDate.ToHashSet() : [];

        // Events of bookings that were cancelled since, and are still there
        var sentIds = sent.Keys.ToList();
        var cancelled = sentIds.Count == 0
            ? []
            : await sql.Queryable<Booking>()
                .Where(b => b.TenantId == tenantId && b.CancelledAt != null && sentIds.Contains(b.Id))
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);
        outOfDate.AddRange(cancelled);

        // One transaction, so the messages are all written or none, and asking again doesn't double them:
        // a message for a booking that is up to date does nothing
        using var tran = sql.Ado.UseTran();
        foreach (var id in outOfDate)
        {
            await OutboxWriter.EnqueueAsync(
                sql,
                tenantId,
                CalendarOutbox.MessageType,
                new CalendarBookingPayload { BookingId = id, Force = forced.Contains(id) },
                cancellationToken
            );
        }

        tran.CommitTran();
        if (outOfDate.Count > 0)
        {
            signal.Notify();
        }

        return outOfDate.Count;
    }
}
