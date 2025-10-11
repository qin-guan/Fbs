using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Booking.ById.Post;

public class Endpoint(
    BookingRepository bookingRepository,
    UserRepository userRepository
) : Endpoint<Request, Entities.Booking>
{
    public override void Configure()
    {
        Post("/Booking/{Id:guid}");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        if (phone is null)
        {
            throw new Exception("User does not exist");
        }

        var booking = await bookingRepository.FindAsync(b => b.Id == req.Id, ct);
        if (booking is null)
        {
            throw new Exception("Booking does not exist.");
        }

        var bookingCreatedBy = await userRepository.FindAsync(u => u.Phone == booking.UserPhone, ct);
        var currentUser = await userRepository.FindAsync(u => u.Phone == phone, ct);

        if (bookingCreatedBy?.Unit != currentUser?.Unit)
        {
            throw new Exception("You are not allowed to update this booking.");
        }

        booking.Conduct = req.Conduct;
        booking.Description = req.Description;
        booking.PocName = req.PocName;
        booking.PocPhone = req.PocPhone;
        booking.UserPhone = phone;

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

        await Send.CreatedAtAsync<Booking.ById.Get.Endpoint>(
            new
            {
                booking.Id
            },
            booking,
            verb: Http.GET,
            cancellation: ct
        );
    }
}