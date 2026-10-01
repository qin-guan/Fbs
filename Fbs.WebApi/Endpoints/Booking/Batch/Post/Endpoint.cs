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
    IUserRepository userRepository,
    IFacilityRepository facilityRepository,
    BookingWriteLock bookingWriteLock,
    BackgroundPublisher publisher
) : Endpoint<Request, List<Entities.Booking>>
{
    /// <summary>
    /// How many bookings are saved to the calendar at once. A few at a time is much quicker than
    /// one by one, without sending the Calendar API a burst of requests.
    /// </summary>
    private const int MaxConcurrentInserts = 4;

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
            var existing = await bookingRepository.GetLatestListAsync(ct);
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

        foreach (var booking in created)
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

        await Send.OkAsync(created, ct);
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
        try
        {
            // Inserts use the request's token rather than the loop's, so when one fails the others
            // finish instead of being cut off part way, leaving nothing half done to roll back
            await Parallel.ForEachAsync(
                bookings,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrentInserts,
                    CancellationToken = ct,
                },
                async (booking, _) => await bookingRepository.InsertAsync(booking, ct)
            );

            return bookings;
        }
        catch (Exception e)
        {
            // Inserting gives a booking its ID, so these are the ones that may have been saved.
            // Every insert has finished by now, as ForEachAsync waits for them before throwing
            var attempted = bookings.Where(b => b.Id != Guid.Empty).ToList();

            logger.LogError(
                e,
                "Batch booking failed after attempting {Attempted} of {Total} bookings, rolling back",
                attempted.Count,
                bookings.Count
            );

            foreach (var booking in attempted)
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
