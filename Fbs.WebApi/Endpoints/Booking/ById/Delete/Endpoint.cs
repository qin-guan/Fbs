using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Dtos;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Booking.ById.Delete;

public class Endpoint(
    BookingRepository bookingRepository,
    UserRepository userRepository
) : Endpoint<Request, BookingWithUser>
{
    public override void Configure()
    {
        Delete("/Booking/{Id:guid}");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var booking = await bookingRepository.FindAsync(b => b.Id == req.Id, ct);
        if (booking is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var phone = User.ClaimValue("Phone");
        if (phone != booking.UserPhone)
        {
            await SendForbiddenAsync(ct);
            return;
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

        await SendNoContentAsync(ct);
    }
}