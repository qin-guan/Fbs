using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Dtos;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Admin.Bookings.Get;

public class Endpoint(UserRepository userRepository, BookingRepository bookingRepository)
    : EndpointWithoutRequest<IEnumerable<BookingWithUser>>
{
    public override void Configure()
    {
        Get("/Admin/Bookings");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        if (phone is null)
        {
            throw new Exception("User does not exist");
        }

        var currentUser = await userRepository.FindAsync(u => u.Phone == phone, ct);
        if (currentUser?.IsAdmin != true)
        {
            throw new Exception("You do not have permission to access admin bookings.");
        }

        var all = await bookingRepository.GetListAsync(ct);
        var users = await userRepository.GetListAsync(ct);

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
            User = users.FirstOrDefault(u => u.Phone == booking.UserPhone),
        });

        await Send.OkAsync(withUser.OrderByDescending(b => b.StartDateTime), ct);
    }
}
