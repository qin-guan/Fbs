using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;
using FluentValidation.Results;

namespace Fbs.WebApi.Endpoints.Booking.Batch.Post;

/// <summary>
/// Creates several bookings with the same details in one go. The batch is all or nothing:
/// if any slot clashes or can't be booked, none of them are created.
/// </summary>
public class Endpoint(
    ILogger<Endpoint> logger,
    BookingRepository bookingRepository,
    UserRepository userRepository,
    FacilityRepository facilityRepository,
    BookingWriteLock bookingWriteLock
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

        if (!bookings.All(BookingRepository.FitsInEventData))
        {
            AddError(r => r.Description, "Event information is too long.", "EX14");
        }

        ThrowIfAnyErrors();

        List<Entities.Booking> created;
        using (await bookingWriteLock.AcquireAsync(ct))
        {
            var existing = await bookingRepository.GetListAsync(ct);
            for (var i = 0; i < bookings.Count; i++)
            {
                var booking = bookings[i];

                var overlapping = existing.FirstOrDefault(b => Overlaps(b, booking));
                if (overlapping is not null)
                {
                    AddSlotError(i, $"Overlaps with booking {overlapping.Id}", "EX10");
                    continue;
                }

                var earlier = bookings.FindIndex(0, i, b => Overlaps(b, booking));
                if (earlier >= 0)
                {
                    AddSlotError(i, $"Overlaps with slot {earlier + 1} in this batch", "EX11");
                }
            }

            ThrowIfAnyErrors();

            created = await InsertAllOrNothingAsync(bookings, ct);
        }

        await Send.OkAsync(created, ct);

        // Finish the response before notifying so large batches don't keep the user waiting,
        // then send the usual Telegram message for each booking.
        await HttpContext.Response.CompleteAsync();

        foreach (var booking in created)
        {
            try
            {
                await PublishAsync(
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
                    },
                    Mode.WaitForAll,
                    CancellationToken.None
                );
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to send notifications for booking {Id}", booking.Id);
            }
        }
    }

    private static bool Overlaps(Entities.Booking a, Entities.Booking b)
    {
        return a.FacilityName == b.FacilityName
            && a.StartDateTime < b.EndDateTime
            && a.EndDateTime > b.StartDateTime;
    }

    private void AddSlotError(int index, string message, string code)
    {
        AddError(new ValidationFailure($"slots[{index}]", message) { ErrorCode = code });
    }

    private async Task<List<Entities.Booking>> InsertAllOrNothingAsync(
        List<Entities.Booking> bookings,
        CancellationToken ct
    )
    {
        var attempted = new List<Entities.Booking>();
        try
        {
            foreach (var booking in bookings)
            {
                attempted.Add(booking);
                await bookingRepository.InsertAsync(booking, ct);
            }

            return attempted;
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "Batch booking failed after {Created} of {Total} bookings, rolling back",
                attempted.Count - 1,
                bookings.Count
            );

            foreach (var booking in attempted.Where(b => b.Id != Guid.Empty))
            {
                try
                {
                    await bookingRepository.RemoveEventsAsync(booking.Id, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    logger.LogError(
                        rollbackException,
                        "Failed to roll back booking {Id}",
                        booking.Id
                    );
                }
            }

            throw;
        }
    }
}
