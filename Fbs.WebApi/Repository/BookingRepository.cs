using System.Linq.Expressions;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
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
                    .Select(item => item.ExtendedProperties.Private__["Data"])
                    .Select(item => JsonSerializer.Deserialize<Booking>(item!))!
            );
        } while (token is not null);

        return bookings;
    }

    public async Task<Booking?> FindAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<Booking> GetAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<Booking> InsertAsync(Booking entity, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetAsync(u => u.Phone == entity.UserPhone, cancellationToken);

        entity.Id = Guid.NewGuid();
        await calendarService.Events.Insert(
                new Event
                {
                    Id = entity.Id.ToString(),
                    Summary = entity.FacilityName,
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
                                   Booked by: {user.Unit} / {user.Name}
                                   Contact Number: {user.Phone?[2..]}

                                   Reason:

                                   {entity.Description}
                                   """,
                    ExtendedProperties = new Event.ExtendedPropertiesData
                    {
                        Private__ = new Dictionary<string, string>()
                        {
                            {
                                "Data", JsonSerializer.Serialize(entity)
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
        throw new NotImplementedException();
    }

    public async Task DeleteAsync(Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}