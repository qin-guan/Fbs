using FastEndpoints;
using Fbs.WebApi.Dtos;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Booking.Get;

public class Endpoint(IUserRepository userRepository, IBookingService bookingService)
    : EndpointWithoutRequest<IEnumerable<BookingWithUser>>
{
    public override void Configure()
    {
        Get("/Booking");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var all = await bookingService.ListAsync(ct);

        var userPhone = Query<string?>("userPhone", false);
        if (userPhone is not null)
        {
            all = all.Where(b => b.UserPhone == userPhone).ToList();
        }

        var startsAfter = Query<DateTime?>("startsAfter", false);
        if (startsAfter is not null)
        {
            all = all.Where(b => b.StartDateTime >= startsAfter).ToList();
        }

        var users = await userRepository.GetByPhoneAsync(ct);
        var withUser = all.Select(booking => new BookingWithUser
        {
            Id = booking.Id,
            FacilityName = booking.FacilityName,
            Conduct = booking.Conduct,
            Description = booking.Description,
            PocName = booking.PocName,
            PocPhone = booking.PocPhone,
            StartDateTime = booking.StartDateTime,
            EndDateTime = booking.EndDateTime,
            User = users.GetValueOrDefault(booking.UserPhone ?? string.Empty),
        });

        await Send.OkAsync(withUser.OrderByDescending(b => b.StartDateTime), ct);
    }
}
