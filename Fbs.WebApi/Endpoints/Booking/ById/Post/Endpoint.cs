using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Booking.ById.Post;

public class Endpoint(
    BookingRepository bookingRepository,
    UserRepository userRepository,
    BookingWriteLock bookingWriteLock
) : Endpoint<Request, Entities.Booking>
{
    public override void Configure()
    {
        Post("/Booking/{Id:guid}");
        Claims("Phone");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");

        var booking = await bookingRepository.FindAsync(b => b.Id == req.Id, ct);
        if (booking is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var bookingCreatedBy = await userRepository.FindAsync(
            u => u.Phone == booking.UserPhone,
            ct
        );
        var currentUser = await userRepository.GetAsync(u => u.Phone == phone, ct);

        if (!currentUser.CanManageBookingsOf(bookingCreatedBy))
        {
            AddError("You can only update bookings made by your unit.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var previousStartDateTime = booking.StartDateTime;
        var previousEndDateTime = booking.EndDateTime;
        var startDateTime = req.StartDateTime ?? booking.StartDateTime;
        var endDateTime = req.EndDateTime ?? booking.EndDateTime;
        var timeChanged =
            startDateTime != previousStartDateTime || endDateTime != previousEndDateTime;

        if (timeChanged)
        {
            ValidateTimeChange(
                previousStartDateTime,
                previousEndDateTime,
                startDateTime!.Value,
                endDateTime!.Value
            );
            ThrowIfAnyErrors();
        }

        var updated = new Entities.Booking
        {
            Id = booking.Id,
            FacilityName = booking.FacilityName,
            StartDateTime = startDateTime,
            EndDateTime = endDateTime,
            Conduct = req.Conduct,
            Description = req.Description,
            PocName = req.PocName,
            PocPhone = req.PocPhone,
            UserPhone = phone,
        };

        if (!BookingRepository.FitsInEventData(updated))
        {
            AddError(r => r.Description, "Event information is too long.");
            ThrowIfAnyErrors();
        }

        using (await bookingWriteLock.AcquireAsync(ct))
        {
            if (timeChanged)
            {
                // Check against the latest bookings, ignoring this booking's current slot
                var bookings = await bookingRepository.GetListAsync(ct);
                var overlapping = bookings.FirstOrDefault(b =>
                    b.Id != updated.Id
                    && b.FacilityName == updated.FacilityName
                    && b.StartDateTime < updated.EndDateTime
                    && b.EndDateTime > updated.StartDateTime
                );

                if (overlapping is not null)
                {
                    AddError(r => r.EndDateTime, $"Overlaps with booking {overlapping.Id}");
                    await Send.ErrorsAsync(cancellation: ct);
                    return;
                }
            }

            booking = await bookingRepository.UpdateAsync(updated, ct);
        }

        await PublishAsync(
            new BookingUpdatedEvent
            {
                Id = booking.Id,
                FacilityName = booking.FacilityName,
                Conduct = booking.Conduct,
                Description = booking.Description,
                PocName = booking.PocName,
                PocPhone = booking.PocPhone,
                StartDateTime = booking.StartDateTime,
                EndDateTime = booking.EndDateTime,
                PreviousStartDateTime = timeChanged ? previousStartDateTime : null,
                PreviousEndDateTime = timeChanged ? previousEndDateTime : null,
                UserPhone = booking.UserPhone,
            },
            Mode.WaitForAll,
            ct
        );

        await Send.CreatedAtAsync<Booking.ById.Get.Endpoint>(
            new { booking.Id },
            booking,
            verb: Http.GET,
            cancellation: ct
        );
    }

    /// <summary>
    /// Bookings that are over can't be moved. A booking that hasn't started can move anywhere in
    /// the future; one that is underway keeps its start and can only have its end changed.
    /// </summary>
    private void ValidateTimeChange(
        DateTimeOffset? previousStartDateTime,
        DateTimeOffset? previousEndDateTime,
        DateTimeOffset startDateTime,
        DateTimeOffset endDateTime
    )
    {
        var now = DateTimeOffset.Now;

        if (previousEndDateTime <= now)
        {
            AddError(
                r => r.StartDateTime,
                "This booking is over, so its time can no longer be changed"
            );
            return;
        }

        if (startDateTime != previousStartDateTime && startDateTime < now)
        {
            AddError(
                r => r.StartDateTime,
                previousStartDateTime <= now
                    ? "This booking has started, so only its end time can be changed"
                    : "Start Date Time must be in the future"
            );
        }

        if (endDateTime <= now)
        {
            AddError(r => r.EndDateTime, "End time must be in the future");
        }
    }
}
