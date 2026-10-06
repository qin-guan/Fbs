using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Bookings;

/// <summary>
/// Keeps bookings in Google Calendar, as they always have been, see <see cref="BookingRepository"/>.
/// </summary>
public sealed class CalendarBookingService(
    ILogger<CalendarBookingService> logger,
    BookingRepository bookingRepository,
    BookingCache bookingCache,
    BookingWriteLock bookingWriteLock
) : IBookingService
{
    /// <summary>
    /// How many bookings are saved to the calendar at once. A few at a time is much quicker than one by
    /// one, without sending the Calendar API a burst of requests.
    /// </summary>
    private const int MaxConcurrentInserts = 4;

    public Task<List<Booking>> ListAsync(CancellationToken cancellationToken = default) =>
        bookingRepository.GetListAsync(cancellationToken);

    public Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        bookingRepository.FindAsync(b => b.Id == id, cancellationToken);

    /// <summary>A calendar event has room for only so much.</summary>
    public bool CanStore(Booking booking) => BookingRepository.FitsInEventData(booking);

    public async Task<CreateResult> CreateAsync(
        IReadOnlyList<Booking> bookings,
        CancellationToken cancellationToken = default
    )
    {
        // Checking for clashes and saving have to happen as one step, or two requests could both pass
        using (await bookingWriteLock.AcquireAsync(cancellationToken))
        {
            var existing = await bookingRepository.GetLatestListAsync(cancellationToken);
            var conflicts = BookingOverlaps.Find(bookings, existing);
            if (conflicts.Count > 0)
            {
                return new CreateResult(conflicts);
            }

            await InsertAllOrNothingAsync(bookings, cancellationToken);
            return new CreateResult([]);
        }
    }

    public async Task<UpdateResult> UpdateAsync(
        Booking updated,
        string updatedByPhone,
        bool checkForClash,
        CancellationToken cancellationToken = default
    )
    {
        using (await bookingWriteLock.AcquireAsync(cancellationToken))
        {
            if (checkForClash)
            {
                // Against the latest bookings, ignoring this booking's current slot
                var bookings = await bookingRepository.GetLatestListAsync(cancellationToken);
                var clash = bookings.FirstOrDefault(b => b.Id != updated.Id && BookingOverlaps.Overlap(b, updated));
                if (clash is not null)
                {
                    return new UpdateResult(null, clash.Id);
                }
            }

            return new UpdateResult(await bookingRepository.UpdateAsync(updated, cancellationToken), null);
        }
    }

    public Task DeleteAsync(Guid id, string cancelledByPhone, CancellationToken cancellationToken = default) =>
        bookingRepository.DeleteAsync(b => b.Id == id, cancellationToken);

    public Task ReloadAsync(CancellationToken cancellationToken = default) => bookingCache.ReloadAsync(cancellationToken);

    private async Task InsertAllOrNothingAsync(IReadOnlyList<Booking> bookings, CancellationToken cancellationToken)
    {
        try
        {
            // Each insert uses the request's cancellation token, not the loop's. One failure must not
            // cancel the others halfway. A finished insert is not rolled back.
            await Parallel.ForEachAsync(
                bookings,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrentInserts,
                    CancellationToken = cancellationToken,
                },
                async (booking, _) => await bookingRepository.InsertAsync(booking, cancellationToken)
            );
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
                    logger.LogError(rollbackException, "Failed to roll back booking {Id}", booking.Id);
                }
            }

            throw;
        }
    }
}
