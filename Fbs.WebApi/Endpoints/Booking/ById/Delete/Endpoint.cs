using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Events;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Booking.ById.Delete;

public class Endpoint(
    IBookingService bookingService,
    IUserRepository userRepository,
    BackgroundPublisher publisher
) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/Booking/{Id:guid}");
        Claims("Phone");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");

        var booking = await bookingService.FindAsync(req.Id, ct);
        if (booking is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var bookedBy = await userRepository.FindAsync(u => u.Phone == booking.UserPhone, ct);
        var currentUser = await userRepository.GetAsync(u => u.Phone == phone, ct);

        // Anyone in the same unit can cancel each other's bookings. Admins can cancel any booking.
        if (!currentUser.IsAdmin && !currentUser.CanManageBookingsOf(bookedBy))
        {
            AddError("You can only cancel bookings made by your unit.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        await bookingService.DeleteAsync(booking.Id, ct);
        publisher.Publish(
            new BookingDeletedEvent
            {
                Id = booking.Id,
                FacilityName = booking.FacilityName,
                Conduct = booking.Conduct,
                Description = booking.Description,
                PocName = booking.PocName,
                PocPhone = booking.PocPhone,
                StartDateTime = booking.StartDateTime,
                EndDateTime = booking.EndDateTime,
                UserPhone = booking.UserPhone,
                CancelledByPhone = phone,
            }
        );

        await Send.NoContentAsync(ct);
    }
}
