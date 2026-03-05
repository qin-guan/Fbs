using System.Linq.Expressions;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Repository;

public class BookingRepository(
    InstrumentationSource instrumentation,
    HybridCache cache,
    IFreeSql freeSql,
    IOptions<GoogleOptions> options,
    CalendarService calendarService,
    UserRepository userRepository,
    ILogger<BookingRepository> logger
) : IRepository<Booking>
{
    public async Task<List<Booking>> GetListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        return await cache.GetOrCreateAsync("Bookings", freeSql, async (state, ct) =>
        {
            return await state.Select<Booking>().ToListAsync(ct);
        }, cancellationToken: cancellationToken);
    }

    public async Task<Booking?> FindAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var list = await GetListAsync(cancellationToken);
        return list.SingleOrDefault(predicate.Compile());
    }

    public async Task<Booking> GetAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var list = await GetListAsync(cancellationToken);
        return list.Single(predicate.Compile());
    }

    public async Task<Booking> InsertAsync(Booking entity, CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var user = await userRepository.GetAsync(u => u.Phone == entity.UserPhone, cancellationToken);

        entity.Id = Guid.NewGuid();

        await freeSql.Insert(entity).ExecuteAffrowsAsync(cancellationToken);

        try
        {
            var @event = CreateCalendarEvent(entity, user);
            await Task.WhenAll([
                calendarService.Events.Insert(@event, options.Value.CarbonCopyCalendarId)
                    .ExecuteAsync(cancellationToken)
            ]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync booking {BookingId} to Google Calendar carbon copy", entity.Id);
        }

        await cache.RemoveAsync("Bookings", cancellationToken);

        return entity;
    }

    public async Task<Booking> UpdateAsync(Booking entity, CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var booking = await freeSql.Select<Booking>()
            .Where(b => b.Id == entity.Id)
            .FirstAsync(cancellationToken);

        booking.StartDateTime = entity.StartDateTime;
        booking.EndDateTime = entity.EndDateTime;
        booking.Conduct = entity.Conduct;
        booking.Description = entity.Description;
        booking.FacilityName = entity.FacilityName;
        booking.PocName = entity.PocName;
        booking.PocPhone = entity.PocPhone;
        booking.UserPhone = entity.UserPhone;

        await freeSql.Update<Booking>()
            .SetSource(booking)
            .ExecuteAffrowsAsync(cancellationToken);

        try
        {
            var user = await userRepository.GetAsync(u => u.Phone == booking.UserPhone, cancellationToken);
            var @event = CreateCalendarEvent(booking, user);
            await Task.WhenAll([
                calendarService.Events.Update(@event, options.Value.CarbonCopyCalendarId, booking.Id.ToString("N"))
                    .ExecuteAsync(cancellationToken)
            ]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync booking {BookingId} update to Google Calendar carbon copy", booking.Id);
        }

        await cache.RemoveAsync("Bookings", cancellationToken);

        return booking;
    }

    public async Task DeleteAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var booking = await GetAsync(predicate, cancellationToken);

        await freeSql.Delete<Booking>()
            .Where(b => b.Id == booking.Id)
            .ExecuteAffrowsAsync(cancellationToken);

        try
        {
            await Task.WhenAll([
                calendarService.Events.Delete(options.Value.CarbonCopyCalendarId, booking.Id.ToString("N"))
                    .ExecuteAsync(cancellationToken)
            ]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync booking {BookingId} deletion to Google Calendar carbon copy", booking.Id);
        }

        await cache.RemoveAsync("Bookings", cancellationToken);
    }

    private static Event CreateCalendarEvent(Booking booking, Entities.User user)
    {
        return new Event
        {
            Id = booking.Id.ToString("N"),
            Summary = $"{user.Unit} {booking.Conduct}",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = booking.StartDateTime,
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = booking.EndDateTime,
            },
            Location = booking.FacilityName,
            Description = $"""
                           Point of contact: {booking.PocName} / {booking.PocPhone}

                           Booked by: {user.Unit} / {user.Name}
                           Number: {user.Phone}

                           Description: 
                           {booking.Description}
                           """,
        };
    }
}