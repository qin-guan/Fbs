using Fbs.WebApi.Entities;

namespace Fbs.WebApi.Bookings;

/// <summary>
/// Where bookings are kept, and the rules for keeping them: a facility is never booked twice at the
/// same time, and bookings made together are made all or not at all.
/// </summary>
public interface IBookingService
{
    /// <summary>Every booking.</summary>
    /// <remarks>The bookings are shared, so they must not be modified.</remarks>
    Task<List<Booking>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>A booking that can be changed without affecting anyone else's view of it, if there is one.</summary>
    Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Whether the booking's details can be stored, which not every store can do for any length.</summary>
    bool CanStore(Booking booking);

    /// <summary>
    /// Makes every one of the bookings, unless any would clash with a booking that is already there or
    /// with an earlier one in the list, in which case it makes none. Gives each booking its ID.
    /// </summary>
    Task<CreateResult> CreateAsync(
        IReadOnlyList<Booking> bookings,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Saves a booking's new details. With <paramref name="checkForClash"/>, as when its time changed,
    /// it isn't saved if it would clash with another booking.
    /// </summary>
    Task<UpdateResult> UpdateAsync(
        Booking updated,
        bool checkForClash,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// A booking that can't be made because the facility is already booked at that time.
/// </summary>
/// <param name="Index">Which of the bookings being made it is.</param>
/// <param name="WithBookingId">The booking it clashes with, if that was there already.</param>
/// <param name="WithEarlierIndex">The earlier booking in the same list it clashes with, if not.</param>
public sealed record BookingConflict(int Index, Guid? WithBookingId, int? WithEarlierIndex);

public sealed record CreateResult(IReadOnlyList<BookingConflict> Conflicts)
{
    public bool Succeeded => Conflicts.Count == 0;
}

/// <param name="Updated">The booking as saved, or null if it wasn't.</param>
/// <param name="ClashesWith">The booking that stopped it being saved.</param>
public sealed record UpdateResult(Booking? Updated, Guid? ClashesWith);
