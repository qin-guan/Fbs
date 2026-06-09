using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Dtos;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Admin.Bookings.ById.Get;

public class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}

public class Endpoint(BookingRepository bookingRepository, UserRepository userRepository)
    : Endpoint<Request, BookingWithUser>
{
    public override void Configure()
    {
        Get("/Admin/Bookings/{Id:guid}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        if (phone is null)
        {
            throw new Exception("User does not exist");
        }

        var currentUser = await userRepository.FindAsync(u => u.Phone == phone, ct);
        if (currentUser?.IsAdmin != true)
        {
            throw new Exception("You do not have permission to access this booking.");
        }

        var booking = await bookingRepository.FindAsync(b => b.Id == req.Id, ct);
        if (booking is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var bookedBy = await userRepository.FindAsync(u => u.Phone == booking.UserPhone, ct);

        await Send.OkAsync(
            new BookingWithUser
            {
                Id = booking.Id,
                FacilityName = booking.FacilityName,
                Conduct = booking.Conduct,
                Description = booking.Description,
                PocName = booking.PocName,
                PocPhone = booking.PocPhone,
                StartDateTime = booking.StartDateTime,
                EndDateTime = booking.EndDateTime,
                User = bookedBy,
            },
            ct
        );
    }
}
