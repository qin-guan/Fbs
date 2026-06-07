using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Admin.Bookings.Put;

public class Request
{
    public Guid Id { get; set; }
    public string? Conduct { get; set; }
    public string? Description { get; set; }
    public string? PocName { get; set; }
    public string? PocPhone { get; set; }
}

public class Endpoint(
    BookingRepository bookingRepository,
    UserRepository userRepository
) : Endpoint<Request, Entities.Booking>
{
    public override void Configure()
    {
        Put("/Admin/Bookings/{Id:guid}");
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
            throw new Exception("You do not have permission to edit bookings.");
        }

        var booking = await bookingRepository.FindAsync(b => b.Id == req.Id, ct);
        if (booking is null)
        {
            throw new Exception("Booking does not exist.");
        }

        booking.Conduct = req.Conduct;
        booking.Description = req.Description;
        booking.PocName = req.PocName;
        booking.PocPhone = req.PocPhone;

        await bookingRepository.UpdateAsync(booking, ct);

        await PublishAsync(new BookingUpdatedEvent
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

        await Send.OkAsync(booking, ct);
    }
}
