using System.Net;
using System.Text.Json;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Telemetry;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using SqlSugar;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.CalendarSync;

/// <summary>
/// Copies a booking to the tenant's Google Calendar: adds its event, changes it, or removes it once the
/// booking is cancelled.
/// </summary>
/// <remarks>
/// <para>
/// The event is the booking as it is now, not as it was when the message was written, so a retry or a
/// message handled out of order still writes the current booking. The event's ID is the booking's ID without
/// dashes, the same ID used when bookings were kept in the calendar, so an event already there is updated
/// and not inserted again.
/// </para>
/// <para>
/// Nothing is read back: changes made to the event in the calendar are replaced by the next change to the
/// booking, or by <see cref="CalendarSyncPlanner"/>.
/// </para>
/// </remarks>
public sealed class CalendarBookingSync(ISqlSugarClient sql, CalendarService calendar, ILogger<CalendarBookingSync> logger)
    : IOutboxHandler
{
    public string Type => CalendarOutbox.MessageType;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = Parse(message);
        var bookingId = payload.BookingId;
        var tenantId = message.TenantId;
        var connection = await sql.Queryable<CalendarConnection>().FirstAsync(c => c.TenantId == tenantId, cancellationToken);
        if (connection is not { Status: CalendarConnectionStatus.Active })
        {
            // Turned off, or failed and not put right yet, which needs someone to
            return;
        }

        var booking = await sql.Queryable<DataBooking>().FirstAsync(b => b.Id == bookingId && b.TenantId == tenantId, cancellationToken);
        if (booking is null)
        {
            throw new OutboxPermanentFailureException($"Booking {bookingId} does not exist.");
        }

        var eventId = bookingId.ToString("N");
        var state = await sql.Queryable<BookingCalendarEvent>().FirstAsync(s => s.BookingId == bookingId, cancellationToken);

        try
        {
            if (booking.CancelledAt is not null)
            {
                // Even without a record of sending it: it may be one that was there before bookings were in the database
                await DeleteAsync(connection.CalendarId, eventId, cancellationToken);
                await sql.Deleteable<BookingCalendarEvent>().Where(s => s.BookingId == bookingId).ExecuteCommandAsync(cancellationToken);
                return;
            }

            if (!payload.Force && state is not null && state.CalendarId == connection.CalendarId && state.SyncedRevision >= booking.Revision)
            {
                return;
            }

            await UpsertAsync(connection, booking, eventId, cancellationToken);
        }
        catch (GoogleApiException e) when (IsPermanent(e))
        {
            await MarkFailedAsync(connection, e, cancellationToken);
            throw new OutboxPermanentFailureException($"Google Calendar refused: {e.Message}", e);
        }

        // If it changed again while it was being sent, what is in the calendar may be older than what another
        // instance sent. Forget what was sent, so that the message for the change sends it again
        var revision = booking.Revision;
        var latest = await sql.Queryable<DataBooking>().Where(b => b.Id == bookingId).Select(b => b.Revision).FirstAsync(cancellationToken);
        if (latest != revision)
        {
            await sql.Deleteable<BookingCalendarEvent>().Where(s => s.BookingId == bookingId).ExecuteCommandAsync(cancellationToken);
            throw new InvalidOperationException($"Booking {bookingId} changed while it was being sent, so it is sent again.");
        }

        await RecordAsync(connection, booking, eventId, revision, cancellationToken);
    }

    private async Task UpsertAsync(CalendarConnection connection, DataBooking booking, string eventId, CancellationToken cancellationToken)
    {
        var body = await BuildEventAsync(booking, eventId, cancellationToken);
        var calendarId = connection.CalendarId;

        try
        {
            await calendar.Events.Update(body, calendarId, eventId).ExecuteAsync(cancellationToken);
            return;
        }
        catch (GoogleApiException e) when (e.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) { }

        try
        {
            await calendar.Events.Insert(body, calendarId).ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.Conflict)
        {
            // The ID is taken by an event that was removed, which is brought back by saying it is confirmed
            logger.LogInformation("Event {EventId} already exists in the calendar, updating it instead", eventId);
            await calendar.Events.Update(body, calendarId, eventId).ExecuteAsync(cancellationToken);
        }
    }

    private async Task DeleteAsync(string calendarId, string eventId, CancellationToken cancellationToken)
    {
        try
        {
            await calendar.Events.Delete(calendarId, eventId).ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException e) when (e.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) { }
    }

    private async Task<Event> BuildEventAsync(DataBooking booking, string eventId, CancellationToken cancellationToken)
    {
        var tenantId = booking.TenantId;
        var facilityId = booking.FacilityId;
        var bookedById = booking.BookedByMemberId;
        var tenant = await sql.Queryable<Tenant>().FirstAsync(t => t.Id == tenantId, cancellationToken);
        var facility = await sql.Queryable<DataFacility>().FirstAsync(f => f.Id == facilityId, cancellationToken);
        var bookedBy = await sql.Queryable<TenantMember>().FirstAsync(m => m.Id == bookedById, cancellationToken);
        var unitId = booking.UnitId ?? bookedBy?.UnitId;
        var unit = unitId is { } id ? (await sql.Queryable<DataUnit>().FirstAsync(u => u.Id == id, cancellationToken))?.Name : null;
        var zone = TenantTimeZone.Of(tenant);

        return new Event
        {
            Id = eventId,
            Status = "confirmed",
            Summary = $"{unit} {booking.Conduct}".Trim(),
            Location = facility?.Name,
            Start = new EventDateTime { DateTimeDateTimeOffset = TimeZoneInfo.ConvertTime(booking.StartUtc, zone), TimeZone = tenant.TimeZone },
            End = new EventDateTime { DateTimeDateTimeOffset = TimeZoneInfo.ConvertTime(booking.EndUtc, zone), TimeZone = tenant.TimeZone },
            Description = $"""
                Point of contact: {booking.PocName} / {booking.PocPhone}

                Booked by: {unit} / {bookedBy?.DisplayName}
                Number: {PhoneNumbers.ToApi(bookedBy?.Phone)}

                Description:
                {booking.Description}
                """,
        };
    }

    private async Task RecordAsync(CalendarConnection connection, DataBooking booking, string eventId, int revision, CancellationToken cancellationToken)
    {
        var bookingId = booking.Id;
        var calendarId = connection.CalendarId;
        var now = DateTimeOffset.UtcNow;
        var updated = await sql.Updateable<BookingCalendarEvent>()
            .SetColumns(s => new BookingCalendarEvent
            {
                CalendarId = calendarId,
                EventId = eventId,
                SyncedRevision = revision,
                SyncedAt = now,
            })
            // Never back to an older version, as another instance may have sent a newer one
            .Where(s => s.BookingId == bookingId && (s.CalendarId != calendarId || s.SyncedRevision < revision))
            .ExecuteCommandAsync(cancellationToken);

        if (updated == 0 && !await sql.Queryable<BookingCalendarEvent>().AnyAsync(s => s.BookingId == bookingId, cancellationToken))
        {
            try
            {
                await sql.Insertable(
                        new BookingCalendarEvent
                        {
                            BookingId = bookingId,
                            TenantId = booking.TenantId,
                            CalendarId = calendarId,
                            EventId = eventId,
                            SyncedRevision = revision,
                            SyncedAt = now,
                        }
                    )
                    .ExecuteCommandAsync(cancellationToken);
            }
            catch (Exception e) when (e.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
            {
                // Another instance recorded it first, and what it recorded is at least as new
            }
        }
    }

    /// <summary>
    /// A Google refusal that will not change on another try, such as a calendar that is not shared with us.
    /// Rate limits and errors on Google's side are worth trying again.
    /// </summary>
    private static bool IsPermanent(GoogleApiException e)
    {
        var reason = e.Error?.Errors?.FirstOrDefault()?.Reason;
        return e.HttpStatusCode switch
        {
            HttpStatusCode.Unauthorized => true,
            HttpStatusCode.NotFound => true,
            HttpStatusCode.Forbidden => reason is not ("rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "dailyLimitExceeded"),
            _ => false,
        };
    }

    private async Task MarkFailedAsync(CalendarConnection connection, GoogleApiException e, CancellationToken cancellationToken)
    {
        var id = connection.Id;
        var active = CalendarConnectionStatus.Active;
        var error = $"{(int)e.HttpStatusCode} {e.Message}";
        error = error.Length <= 1000 ? error : error[..1000];
        var stopped = await sql.Updateable<CalendarConnection>()
            .SetColumns(c => new CalendarConnection { Status = CalendarConnectionStatus.Failed, LastError = error })
            .Where(c => c.Id == id && c.Status == active)
            .ExecuteCommandAsync(cancellationToken);
        if (stopped > 0)
        {
            FbsMetrics.CalendarsFailed.Add(1);
        }

        logger.LogError(e, "Google Calendar refused tenant {TenantId}'s calendar, so it is no longer sent to", connection.TenantId);
    }

    private static CalendarBookingPayload Parse(OutboxMessage message)
    {
        try
        {
            return JsonSerializer.Deserialize<CalendarBookingPayload>(message.Payload)
                ?? throw new OutboxPermanentFailureException("The message is empty.");
        }
        catch (JsonException e)
        {
            throw new OutboxPermanentFailureException("The message could not be read.", e);
        }
    }
}
