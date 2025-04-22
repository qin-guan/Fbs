using System.Linq.Expressions;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using MemoryPack;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Repository;

public class BookingRepository(
    IOptions<GoogleOptions> options,
    CalendarService calendarService,
    UserRepository userRepository
) : IRepository<Booking>
{
    public async Task<List<Booking>> GetListAsync(CancellationToken cancellationToken = default)
    {
        string? token;
        var bookings = new List<Booking>();

        do
        {
            var items = await calendarService.Events.List(options.Value.CalendarId).ExecuteAsync(cancellationToken);
            token = items.NextPageToken;

            bookings.AddRange(
                items.Items
                    .Select(item => item.ExtendedProperties.Shared["Data"])
                    .Select(Convert.FromBase64String)
                    .Select(item => MemoryPackSerializer.Deserialize<Booking>(item))!
            );
        } while (token is not null);

        return bookings;
    }

    public async Task<Booking?> FindAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        var list = await GetListAsync(cancellationToken);
        return list.SingleOrDefault(predicate.Compile());
    }

    public async Task<Booking> GetAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        var list = await GetListAsync(cancellationToken);
        return list.Single(predicate.Compile());
    }

    public async Task<Booking> InsertAsync(Booking entity, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetAsync(u => u.Phone == entity.UserPhone, cancellationToken);

        entity.Id = Guid.NewGuid();
        var data = Convert.ToBase64String(MemoryPackSerializer.Serialize(entity));

        if (data.Length > 1000)
        {
            throw new Exception("Event information is too long.");
        }

        await calendarService.Events.Insert(
                new Event
                {
                    Id = entity.Id.ToString("N"),
                    Summary = $"{user.Unit} {entity.Conduct}",
                    Start = new EventDateTime
                    {
                        DateTimeDateTimeOffset = entity.StartDateTime,
                    },
                    End = new EventDateTime
                    {
                        DateTimeDateTimeOffset = entity.EndDateTime,
                    },
                    Location = entity.FacilityName,
                    Description = $"""
                                   Point of contact: {entity.PocName} / {entity.PocPhone}

                                   Booked by: {user.Unit} / {user.Name}
                                   Number: {user.Phone?[2..]}

                                   Description: 
                                   {entity.Description}
                                   """,
                    ExtendedProperties = new Event.ExtendedPropertiesData
                    {
                        Shared = new Dictionary<string, string>()
                        {
                            {
                                "Data", data
                            }
                        }
                    }
                },
                options.Value.CalendarId
            )
            .ExecuteAsync(cancellationToken);

        return entity;
    }

    public async Task<Booking> UpdateAsync(Booking entity, CancellationToken cancellationToken = default)
    {
        var bookings = await GetListAsync(cancellationToken);
        var booking = bookings.Single(b => b.Id == entity.Id);

        booking.StartDateTime = entity.StartDateTime;
        booking.EndDateTime = entity.EndDateTime;
        booking.Conduct = entity.Conduct;
        booking.Description = entity.Description;
        booking.FacilityName = entity.FacilityName;
        booking.PocName = entity.PocName;
        booking.PocPhone = entity.PocPhone;
        booking.UserPhone = entity.UserPhone;

        var data = Convert.ToBase64String(MemoryPackSerializer.Serialize(booking));

        if (data.Length > 1000)
        {
            throw new Exception("Event information is too long.");
        }

        var user = await userRepository.GetAsync(u => u.Phone == booking.UserPhone, cancellationToken);

        await calendarService.Events.Update(
                new Event
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
                                   Number: {user.Phone?[2..]}

                                   Description: 
                                   {booking.Description}
                                   """,
                    ExtendedProperties = new Event.ExtendedPropertiesData
                    {
                        Shared = new Dictionary<string, string>()
                        {
                            {
                                "Data", data
                            }
                        }
                    }
                },
                options.Value.CalendarId,
                booking.Id.ToString("N")
            )
            .ExecuteAsync(cancellationToken);

        return booking;
    }

    public async Task DeleteAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetAsync(predicate, cancellationToken);
        await calendarService.Events.Delete(options.Value.CalendarId, booking.Id.ToString("N"))
            .ExecuteAsync(cancellationToken);
    }
}