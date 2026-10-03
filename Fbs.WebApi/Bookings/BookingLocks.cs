using Fbs.WebApi.Data.Entities;
using SqlSugar;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Bookings;

/// <summary>
/// What everything that books a facility, or moves a booking, does before it decides anything, see
/// <see cref="DatabaseBookingService"/> for why.
/// </summary>
internal static class BookingLocks
{
    /// <summary>
    /// Takes the facilities' rows, holding them until the transaction ends. They are taken in order of
    /// their IDs, so two requests that need the same facilities can't each hold one the other is waiting for.
    /// </summary>
    public static async Task LockFacilitiesAsync(ISqlSugarClient sql, Guid tenantId, List<Guid> facilityIds, CancellationToken cancellationToken)
    {
        await sql.Queryable<DataFacility>()
            .Where(f => f.TenantId == tenantId && facilityIds.Contains(f.Id))
            .OrderBy(f => f.Id)
            .TranLock(DbLockType.Wait)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The bookings that overlap the times, read so that they are as they are now and stay as they are
    /// until the transaction ends.
    /// </summary>
    public static async Task<List<DataBooking>> FindBookingsBetweenAsync(
        ISqlSugarClient sql,
        Guid tenantId,
        List<Guid> facilityIds,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? excluding,
        CancellationToken cancellationToken
    )
    {
        var except = excluding ?? Guid.Empty;
        return await sql.Queryable<DataBooking>()
            .Where(b =>
                b.TenantId == tenantId
                && facilityIds.Contains(b.FacilityId)
                && b.CancelledAt == null
                && b.StartUtc < to
                && b.EndUtc > from
                && b.Id != except
            )
            .TranLock(DbLockType.Wait)
            .ToListAsync(cancellationToken);
    }
}
