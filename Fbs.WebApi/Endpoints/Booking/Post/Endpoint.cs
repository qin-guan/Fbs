using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Booking.Post;

public class Endpoint(
    BookingRepository bookingRepository,
    UserRepository userRepository,
    FacilityRepository facilityRepository
) : Endpoint<Request, Entities.Booking>
{
    public override void Configure()
    {
        Post("/Booking");
        Claims("Phone");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        
        var facility = await facilityRepository.FindAsync(f => f.Name == req.FacilityName, ct);
        if (facility?.Scope is null)
        {
            await SendNotFoundAsync(cancellation: ct);
            return;
        }

        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
        if (user.Unit is null)
        {
            throw new Exception("User must have unit specified in order to book facilities.");
        }

        if (!facility.AvailableForAll && !facility.Scope.Contains(user.Unit))
        {
            AddError(r => r.FacilityName, $"You do not have permission to book this facility");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var bookings = await bookingRepository.GetListAsync(ct);
        var overlapping = bookings.FirstOrDefault(b =>
            b.FacilityName == facility.Name &&
            b.StartDateTime < req.EndDateTime &&
            b.EndDateTime > req.StartDateTime
        );

        if (overlapping is not null)
        {
            AddError(r => r.EndDateTime, $"Overlaps with booking {overlapping.Id}");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var booking = new Entities.Booking
        {
            StartDateTime = req.StartDateTime,
            EndDateTime = req.EndDateTime,
            Conduct = req.Conduct,
            Description = req.Description,
            FacilityName = req.FacilityName,
            PocName = req.PocName,
            PocPhone = req.PocPhone,
            UserPhone = phone
        };

        await bookingRepository.InsertAsync(booking, ct);

        await PublishAsync(new BookingCreatedEvent
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

        await SendCreatedAtAsync<Booking.ById.Get.Endpoint>(
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