using System.Linq.Expressions;
using System.Net;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using MemoryPack;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Repository;

public class BookingRepository(
    InstrumentationSource instrumentation,
    BookingCache bookingCache,
    IOptions<GoogleOptions> options,
    CalendarService calendarService,
    UserRepository userRepository
) : IRepository<Booking>
{
    private const int MaxEventDataLength = 1000;

    /// <summary>
    /// Whether the booking fits in the data embedded in its calendar event.
    /// </summary>
    public static bool FitsInEventData(Booking entity)
    {
        return Convert.ToBase64String(MemoryPackSerializer.Serialize(entity)).Length
            <= MaxEventDataLength;
    }

    /// <remarks>The bookings are shared, so they must not be modified.</remarks>
    public async Task<List<Booking>> GetListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        return [.. await bookingCache.GetAsync(cancellationToken)];
    }

    /// <summary>
    /// Like <see cref="GetListAsync"/>, but first fetches changes made to the calendar since it
    /// was last read. Use this when the bookings must be current, such as when checking for clashes.
    /// </summary>
    /// <remarks>The bookings are shared, so they must not be modified.</remarks>
    public async Task<List<Booking>> GetLatestListAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        return [.. await bookingCache.GetLatestAsync(cancellationToken)];
    }

    public async Task<Booking?> FindAsync(
        Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var list = await bookingCache.GetAsync(cancellationToken);
        // A copy, so callers can change it without affecting the cache
        return list.SingleOrDefault(predicate.Compile())?.Clone();
    }

    public async Task<Booking> GetAsync(
        Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var list = await bookingCache.GetAsync(cancellationToken);
        // A copy, so callers can change it without affecting the cache
        return list.Single(predicate.Compile()).Clone();
    }

    public async Task<Booking> InsertAsync(
        Booking entity,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var user = await userRepository.GetAsync(
            u => u.Phone == entity.UserPhone,
            cancellationToken
        );

        entity.Id = Guid.NewGuid();
        var data = Convert.ToBase64String(MemoryPackSerializer.Serialize(entity));

        if (data.Length > MaxEventDataLength)
        {
            throw new Exception("Event information is too long.");
        }

        var @event = new Event
        {
            Id = entity.Id.ToString("N"),
            Summary = $"{user.Unit} {entity.Conduct}",
            Start = new EventDateTime { DateTimeDateTimeOffset = entity.StartDateTime },
            End = new EventDateTime { DateTimeDateTimeOffset = entity.EndDateTime },
            Location = entity.FacilityName,
            Description = $"""
                Point of contact: {entity.PocName} / {entity.PocPhone}

                Booked by: {user.Unit} / {user.Name}
                Number: {user.Phone}

                Description: 
                {entity.Description}
                """,
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Shared = new Dictionary<string, string>() { { "Data", data } },
            },
        };

        await Task.WhenAll([
            calendarService
                .Events.Insert(@event, options.Value.CalendarId)
                .ExecuteAsync(cancellationToken),
            calendarService
                .Events.Insert(@event, options.Value.CarbonCopyCalendarId)
                .ExecuteAsync(cancellationToken),
        ]);

        await bookingCache.SetAsync(entity.Clone());

        return entity;
    }

    public async Task<Booking> UpdateAsync(
        Booking entity,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var bookings = await bookingCache.GetAsync(cancellationToken);
        var booking = bookings.Single(b => b.Id == entity.Id).Clone();

        booking.StartDateTime = entity.StartDateTime;
        booking.EndDateTime = entity.EndDateTime;
        booking.Conduct = entity.Conduct;
        booking.Description = entity.Description;
        booking.FacilityName = entity.FacilityName;
        booking.PocName = entity.PocName;
        booking.PocPhone = entity.PocPhone;
        booking.UserPhone = entity.UserPhone;

        var data = Convert.ToBase64String(MemoryPackSerializer.Serialize(booking));

        if (data.Length > MaxEventDataLength)
        {
            throw new Exception("Event information is too long.");
        }

        var user = await userRepository.GetAsync(
            u => u.Phone == booking.UserPhone,
            cancellationToken
        );
        var @event = new Event
        {
            Id = booking.Id.ToString("N"),
            Summary = $"{user.Unit} {booking.Conduct}",
            Start = new EventDateTime { DateTimeDateTimeOffset = booking.StartDateTime },
            End = new EventDateTime { DateTimeDateTimeOffset = booking.EndDateTime },
            Location = booking.FacilityName,
            Description = $"""
                Point of contact: {booking.PocName} / {booking.PocPhone}

                Booked by: {user.Unit} / {user.Name}
                Number: {user.Phone}

                Description: 
                {booking.Description}
                """,
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Shared = new Dictionary<string, string>() { { "Data", data } },
            },
        };

        await Task.WhenAll([
            calendarService
                .Events.Update(@event, options.Value.CalendarId, booking.Id.ToString("N"))
                .ExecuteAsync(cancellationToken),
            calendarService
                .Events.Update(@event, options.Value.CarbonCopyCalendarId, booking.Id.ToString("N"))
                .ExecuteAsync(cancellationToken),
        ]);

        await bookingCache.SetAsync(booking.Clone());

        return booking;
    }

    public async Task DeleteAsync(
        Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var booking = await GetAsync(predicate, cancellationToken);

        await Task.WhenAll([
            calendarService
                .Events.Delete(options.Value.CalendarId, booking.Id.ToString("N"))
                .ExecuteAsync(cancellationToken),
            calendarService
                .Events.Delete(options.Value.CarbonCopyCalendarId, booking.Id.ToString("N"))
                .ExecuteAsync(cancellationToken),
        ]);

        await bookingCache.RemoveAsync(booking.Id);
    }

    /// <summary>
    /// Removes a booking's events from both calendars, skipping calendars that don't have it.
    /// Used to undo a partially created booking.
    /// </summary>
    public async Task RemoveEventsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        foreach (var calendarId in new[] { options.Value.CalendarId, options.Value.CarbonCopyCalendarId })
        {
            try
            {
                await calendarService
                    .Events.Delete(calendarId, id.ToString("N"))
                    .ExecuteAsync(cancellationToken);
            }
            catch (GoogleApiException e)
                when (e.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) { }
        }

        await bookingCache.RemoveAsync(id);
    }
}
