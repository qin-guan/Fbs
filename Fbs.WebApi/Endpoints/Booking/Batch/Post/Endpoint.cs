using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;
using FluentValidation.Results;

namespace Fbs.WebApi.Endpoints.Booking.Batch.Post;

/// <summary>
/// Creates several bookings with the same details in one go. The batch is all or nothing:
/// if any slot clashes or can't be booked, none of them are created.
/// </summary>
public class Endpoint(
    IBookingService bookingService,
    IUserRepository userRepository,
    IFacilityRepository facilityRepository,
    BackgroundPublisher publisher
) : Endpoint<Request, List<Entities.Booking>>
{
    public override void Configure()
    {
        Post("/Booking/Batch");
        Claims("Phone");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");

        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
        if (user.Unit is null)
        {
            throw new Exception("User must have unit specified in order to book facilities.");
        }

        var facilities = await facilityRepository.GetListAsync(ct);
        for (var i = 0; i < req.Slots.Count; i++)
        {
            var facility = facilities.SingleOrDefault(f => f.Name == req.Slots[i].FacilityName);
            if (facility?.Scope is null)
            {
                AddSlotError(i, $"{req.Slots[i].FacilityName} does not exist", "EX12");
            }
            else if (!facility.AvailableForAll && !facility.Scope.Contains(user.Unit))
            {
                AddSlotError(i, $"You do not have permission to book {facility.Name}", "EX13");
            }
        }

        ThrowIfAnyErrors();

        var bookings = req
            .Slots.Select(slot => new Entities.Booking
            {
                StartDateTime = slot.StartDateTime,
                EndDateTime = slot.EndDateTime,
                Conduct = req.Conduct,
                Description = req.Description,
                FacilityName = slot.FacilityName,
                PocName = req.PocName,
                PocPhone = req.PocPhone,
                UserPhone = phone,
            })
            .ToList();

        if (!bookings.All(bookingService.CanStore))
        {
            AddError(r => r.Description, "Event information is too long.", "EX14");
        }

        ThrowIfAnyErrors();

        var result = await bookingService.CreateAsync(bookings, ct);
        foreach (var conflict in result.Conflicts)
        {
            if (conflict.WithBookingId is { } existingId)
            {
                AddSlotError(conflict.Index, $"Overlaps with booking {existingId}", "EX10");
            }
            else
            {
                AddSlotError(conflict.Index, $"Overlaps with slot {conflict.WithEarlierIndex + 1} in this batch", "EX11");
            }
        }

        ThrowIfAnyErrors();

        foreach (var booking in bookings)
        {
            publisher.Publish(
                new BookingCreatedEvent
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
                }
            );
        }

        await Send.OkAsync(bookings, ct);
    }

    private void AddSlotError(int index, string message, string code)
    {
        AddError(new ValidationFailure($"slots[{index}]", message) { ErrorCode = code });
    }
}
