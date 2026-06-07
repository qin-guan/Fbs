using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Dtos;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Admin.Bookings.Delete;

public class Request
{
    public Guid Id { get; set; }
}

public class Endpoint(
    BookingRepository bookingRepository,
    UserRepository userRepository
) : Endpoint<Request, BookingWithUser>
{
    public override void Configure()
    {
        Delete("/Admin/Bookings/{Id:guid}");
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
            throw new Exception("You do not have permission to delete bookings.");
        }

        var booking = await bookingRepository.FindAsync(b => b.Id == req.Id, ct);
        if (booking is null)
        {
            throw new Exception("Booking does not exist.");
        }

        await bookingRepository.DeleteAsync(b => b.Id == booking.Id, ct);
        
        await PublishAsync(new BookingDeletedEvent
        {
            Id = booking.Id,
            FacilityName = booking.FacilityName,
            Conduct = booking.Conduct,
            Description = booking.Description,
            PocName = booking.PocName,
            PocPhone = booking.PocPhone,
            StartDateTime = booking.StartDateTime,
            EndDateTime = booking.EndDateTime,
            UserPhone = booking.UserPhone
        }, Mode.WaitForAll, ct);

        await Send.NoContentAsync(ct);
    }
}
