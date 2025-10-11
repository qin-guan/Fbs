using FastEndpoints;
using Fbs.WebApi.Dtos;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Booking.ById.Get;

public class Endpoint(
    BookingRepository bookingRepository,
    UserRepository userRepository
) : Endpoint<Request, BookingWithUser>
{
    public override void Configure()
    {
        Get("/Booking/{Id:guid}");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var booking = await bookingRepository.FindAsync(b => b.Id == req.Id, ct);
        if (booking is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        
        var user = await userRepository.GetAsync(u => u.Phone == booking.UserPhone, ct);
        await Send.OkAsync(new BookingWithUser
        {
            Id = booking.Id,
            FacilityName = booking.FacilityName,
            Conduct = booking.Conduct,
            Description = booking.Description,
            PocName = booking.PocName,
            PocPhone = booking.PocPhone,
            StartDateTime = booking.StartDateTime,
            EndDateTime = booking.EndDateTime,
            User = user,
        }, ct);
    }
}