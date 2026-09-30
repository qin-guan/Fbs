using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Notifications;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Telemetry;
using SqlSugar;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;

namespace Fbs.WebApi.Bookings;

/// <summary>What is written on a booking, apart from when and where.</summary>
public sealed record BookingDetails(string Conduct, string? Description, string? PocName, string? PocPhone);

public sealed record NewSlot(Guid FacilityId, DateTimeOffset Start, DateTimeOffset End);

public sealed record TenantCreateResult(IReadOnlyList<DataBooking> Created, IReadOnlyList<BookingConflict> Conflicts)
{
    public bool Succeeded => Conflicts.Count == 0;
}

/// <param name="Updated">The booking as saved, or null if it wasn't.</param>
/// <param name="ClashesWith">The booking that stopped it being saved.</param>
/// <param name="NotFound">Whether it was cancelled, or gone, by the time it was locked.</param>
public sealed record TenantUpdateResult(DataBooking? Updated, Guid? ClashesWith, bool NotFound = false);

/// <summary>
/// The bookings of an organisation, made by its members. Who is making a change, and which organisation, are
/// given, never looked up here, and every read and write is of that organisation's bookings only.
/// </summary>
/// <remarks>
/// The facility is never booked twice at the same time for the same reason as in
/// <see cref="DatabaseBookingService"/>: whatever books it, or moves a booking, locks its row first, and then
/// looks for a clash with a locking read, see <see cref="BookingLocks"/>.
/// </remarks>
public sealed class TenantBookings(ISqlSugarClient sql, OutboxSignal signal)
{
    /// <summary>The bookings that share any time with the window, earliest first, cancelled ones left out.</summary>
    public async Task<List<DataBooking>> ListAsync(
        Guid tenantId,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? facilityId,
        Guid? bookedByMemberId,
        int limit,
        CancellationToken ct
    )
    {
        var query = sql.Queryable<DataBooking>()
            .Where(b => b.TenantId == tenantId && b.CancelledAt == null && b.StartUtc < to && b.EndUtc > from)
            .WhereIF(facilityId is not null, b => b.FacilityId == facilityId)
            .WhereIF(bookedByMemberId is not null, b => b.BookedByMemberId == bookedByMemberId);

        return await query.OrderBy(b => b.StartUtc).OrderBy(b => b.Id).Take(limit).ToListAsync(ct);
    }

    public Task<DataBooking?> FindAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        sql.Queryable<DataBooking>().FirstAsync(b => b.Id == id && b.TenantId == tenantId && b.CancelledAt == null, ct)!;

    /// <summary>
    /// Makes every one of the slots a booking of <paramref name="booker"/>'s, or none of them if any would clash with
    /// a booking that is there or with an earlier slot.
    /// </summary>
    public Task<TenantCreateResult> CreateAsync(
        Guid tenantId,
        TenantMember booker,
        BookingDetails details,
        IReadOnlyList<NewSlot> slots,
        CancellationToken ct
    ) => TransactionRetry.RunAsync(() => CreateOnceAsync(tenantId, booker, details, slots, ct), ct);

    private async Task<TenantCreateResult> CreateOnceAsync(
        Guid tenantId,
        TenantMember booker,
        BookingDetails details,
        IReadOnlyList<NewSlot> slots,
        CancellationToken ct
    )
    {
        var batchId = slots.Count > 1 ? Guid.NewGuid() : (Guid?)null;
        var now = DateTimeOffset.UtcNow;
        var rows = slots
            .Select(slot => new DataBooking
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FacilityId = slot.FacilityId,
                StartUtc = slot.Start.ToUniversalTime(),
                EndUtc = slot.End.ToUniversalTime(),
                Conduct = details.Conduct,
                Description = details.Description,
                PocName = details.PocName,
                PocPhone = details.PocPhone,
                BookedByMemberId = booker.Id,
                UnitId = booker.UnitId,
                BatchId = batchId,
                Revision = 1,
                CreatedAt = now,
                UpdatedAt = now,
            })
            .ToList();
        var facilityIds = rows.Select(r => r.FacilityId).Distinct().Order().ToList();

        // Disposing the transaction without committing it rolls it back, and lets go of the locks
        using var tran = sql.Ado.UseTran();
        await BookingLocks.LockFacilitiesAsync(sql, tenantId, facilityIds, ct);

        var existing = await BookingLocks.FindBookingsBetweenAsync(sql, tenantId, facilityIds, rows.Min(r => r.StartUtc), rows.Max(r => r.EndUtc), excluding: null, ct);
        var conflicts = FindConflicts(rows, existing);
        if (conflicts.Count > 0)
        {
            FbsMetrics.BookingClashes.Add(1);
            return new TenantCreateResult([], conflicts);
        }

        await sql.Insertable(rows).ExecuteCommandAsync(ct);
        await OutboxWriter.EnqueueAsync(
            sql,
            tenantId,
            TelegramBookingNotifier.MessageType,
            new TelegramBookingPayload { Change = BookingChange.Created, BookingIds = rows.Select(r => r.Id).ToList(), ActorMemberId = booker.Id },
            ct
        );
        await CalendarOutbox.EnqueueAsync(sql, tenantId, rows.Select(r => r.Id), ct);
        tran.CommitTran();
        signal.Notify();
        FbsMetrics.BookingsMade.Add(rows.Count);

        return new TenantCreateResult(rows, []);
    }

    /// <summary>
    /// Saves a booking's new details, and its new time if it is given. Who made it, and where it is, never change.
    /// </summary>
    public Task<TenantUpdateResult> UpdateAsync(
        Guid tenantId,
        TenantMember editor,
        Guid bookingId,
        BookingDetails details,
        DateTimeOffset? start,
        DateTimeOffset? end,
        CancellationToken ct
    ) => TransactionRetry.RunAsync(() => UpdateOnceAsync(tenantId, editor, bookingId, details, start, end, ct), ct);

    private async Task<TenantUpdateResult> UpdateOnceAsync(
        Guid tenantId,
        TenantMember editor,
        Guid bookingId,
        BookingDetails details,
        DateTimeOffset? start,
        DateTimeOffset? end,
        CancellationToken ct
    )
    {
        // Which facility to lock has to be known before locking, and locking has to come before the booking itself
        // is, or this and a booking being made could each be waiting for the other
        var known = await FindAsync(tenantId, bookingId, ct);
        if (known is null)
        {
            return new TenantUpdateResult(null, null, NotFound: true);
        }

        using var tran = sql.Ado.UseTran();
        await BookingLocks.LockFacilitiesAsync(sql, tenantId, [known.FacilityId], ct);

        var row = await sql.Queryable<DataBooking>().Where(b => b.Id == bookingId && b.TenantId == tenantId).TranLock(DbLockType.Wait).FirstAsync(ct);
        if (row is null || row.CancelledAt is not null)
        {
            return new TenantUpdateResult(null, null, NotFound: true);
        }

        var newStart = start?.ToUniversalTime() ?? row.StartUtc;
        var newEnd = end?.ToUniversalTime() ?? row.EndUtc;

        // Whether the time moved is worked out here rather than trusted, as it can have moved since the caller looked
        var timeChanged = row.StartUtc != newStart || row.EndUtc != newEnd;
        if (timeChanged)
        {
            var clash = (await BookingLocks.FindBookingsBetweenAsync(sql, tenantId, [row.FacilityId], newStart, newEnd, excluding: bookingId, ct)).FirstOrDefault();
            if (clash is not null)
            {
                FbsMetrics.BookingClashes.Add(1);
                return new TenantUpdateResult(null, clash.Id);
            }
        }

        var previousStart = row.StartUtc;
        var previousEnd = row.EndUtc;
        row.StartUtc = newStart;
        row.EndUtc = newEnd;
        row.Conduct = details.Conduct;
        row.Description = details.Description;
        row.PocName = details.PocName;
        row.PocPhone = details.PocPhone;
        row.UpdatedByMemberId = editor.Id;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.Revision++;

        // Only these, so who made the booking, and what for, can't be changed by it
        await sql.Updateable(row)
            .UpdateColumns(b => new
            {
                b.StartUtc,
                b.EndUtc,
                b.Conduct,
                b.Description,
                b.PocName,
                b.PocPhone,
                b.UpdatedByMemberId,
                b.UpdatedAt,
                b.Revision,
            })
            .ExecuteCommandAsync(ct);
        await OutboxWriter.EnqueueAsync(
            sql,
            tenantId,
            TelegramBookingNotifier.MessageType,
            new TelegramBookingPayload
            {
                Change = BookingChange.Updated,
                BookingIds = [row.Id],
                ActorMemberId = editor.Id,
                PreviousStartUtc = timeChanged ? previousStart : null,
                PreviousEndUtc = timeChanged ? previousEnd : null,
            },
            ct
        );
        await CalendarOutbox.EnqueueAsync(sql, tenantId, [row.Id], ct);
        tran.CommitTran();
        signal.Notify();
        FbsMetrics.BookingsChanged.Add(1);

        return new TenantUpdateResult(row, null);
    }

    /// <summary>Cancels it, and keeps it. Cancelling something already cancelled changes nothing, and tells nobody.</summary>
    /// <returns>Whether it was cancelled by this.</returns>
    public Task<bool> CancelAsync(Guid tenantId, TenantMember cancelledBy, Guid bookingId, CancellationToken ct) =>
        TransactionRetry.RunAsync(() => CancelOnceAsync(tenantId, cancelledBy, bookingId, ct), ct);

    private async Task<bool> CancelOnceAsync(Guid tenantId, TenantMember cancelledBy, Guid bookingId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cancelledById = cancelledBy.Id;

        using var tran = sql.Ado.UseTran();
        var cancelled = await sql.Updateable<DataBooking>()
            .SetColumns(b => new DataBooking
            {
                CancelledAt = now,
                CancelledByMemberId = cancelledById,
                UpdatedByMemberId = cancelledById,
                UpdatedAt = now,
                Revision = b.Revision + 1,
            })
            .Where(b => b.Id == bookingId && b.TenantId == tenantId && b.CancelledAt == null)
            .ExecuteCommandAsync(ct);
        if (cancelled == 0)
        {
            return false;
        }

        await OutboxWriter.EnqueueAsync(
            sql,
            tenantId,
            TelegramBookingNotifier.MessageType,
            new TelegramBookingPayload { Change = BookingChange.Cancelled, BookingIds = [bookingId], ActorMemberId = cancelledById },
            ct
        );
        await CalendarOutbox.EnqueueAsync(sql, tenantId, [bookingId], ct);
        tran.CommitTran();
        signal.Notify();
        FbsMetrics.BookingsCancelled.Add(1);
        return true;
    }

    /// <summary>
    /// Every one of the new bookings that clashes with one that is there already, or failing that with an earlier one
    /// in the list. Bookings that touch, one ending as the next starts, don't clash.
    /// </summary>
    private static List<BookingConflict> FindConflicts(IReadOnlyList<DataBooking> bookings, IReadOnlyList<DataBooking> existing)
    {
        static bool Overlap(DataBooking a, DataBooking b) => a.FacilityId == b.FacilityId && a.StartUtc < b.EndUtc && a.EndUtc > b.StartUtc;

        var conflicts = new List<BookingConflict>();
        for (var i = 0; i < bookings.Count; i++)
        {
            var booking = bookings[i];
            var there = existing.FirstOrDefault(b => Overlap(b, booking));
            if (there is not null)
            {
                conflicts.Add(new BookingConflict(i, there.Id, null));
                continue;
            }

            var earlier = Enumerable.Range(0, i).Cast<int?>().FirstOrDefault(j => Overlap(bookings[j!.Value], booking));
            if (earlier is not null)
            {
                conflicts.Add(new BookingConflict(i, null, earlier));
            }
        }

        return conflicts;
    }
}
