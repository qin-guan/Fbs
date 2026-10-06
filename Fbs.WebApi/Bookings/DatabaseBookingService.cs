using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Notifications;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Repository.Database;
using Fbs.WebApi.Telemetry;
using SqlSugar;
using Booking = Fbs.WebApi.Entities.Booking;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Bookings;

/// <summary>
/// Keeps bookings in the database, for the tenant named by <c>Storage:TenantSlug</c>.
/// </summary>
/// <remarks>
/// <para>
/// A facility is never booked twice at once because everything that could book it, or move a booking
/// onto it, first takes a lock on the facility's row, and only then looks for a clash. Looking has to be
/// a locking read (<c>FOR UPDATE</c>): TiDB reads inside a transaction as of when it began, so an ordinary
/// read after waiting for the lock still can't see what the transaction that held it has just booked,
/// and both go ahead. A locking read sees what has been committed. Nothing locks the gap a new booking
/// would go into, which is why it is the facility that is locked. See docs/adr/spikes/s3-tidb-concurrency.
/// </para>
/// <para>
/// Whoever made a booking is fixed. A change records who made it as <c>UpdatedByMemberId</c>, and
/// cancelling keeps the booking, marked as cancelled, so nothing that refers to it is left dangling.
/// </para>
/// </remarks>
public sealed class DatabaseBookingService(ISqlSugarClient sql, DefaultTenant tenant, OutboxSignal signal) : IBookingService
{
    // The most each column holds, see the Booking entity
    private const int ConductLength = 200;
    private const int DescriptionLength = 2000;
    private const int PocNameLength = 200;
    private const int PocPhoneLength = 32;

    public async Task<List<Booking>> ListAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadAsync(cancellationToken);
        var tenantId = snapshot.Tenant.Id;
        var rows = await sql.Queryable<DataBooking>()
            .Where(b => b.TenantId == tenantId && b.CancelledAt == null)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(snapshot.ToBooking).ToList();
    }

    public async Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadAsync(cancellationToken);
        var tenantId = snapshot.Tenant.Id;
        var row = await sql.Queryable<DataBooking>()
            .FirstAsync(b => b.Id == id && b.TenantId == tenantId && b.CancelledAt == null, cancellationToken);

        return row is null ? null : snapshot.ToBooking(row);
    }

    public bool CanStore(Booking booking) =>
        (booking.Conduct?.Length ?? 0) <= ConductLength
        && (booking.Description?.Length ?? 0) <= DescriptionLength
        && (booking.PocName?.Length ?? 0) <= PocNameLength
        && (booking.PocPhone?.Length ?? 0) <= PocPhoneLength;

    public Task<CreateResult> CreateAsync(
        IReadOnlyList<Booking> bookings,
        CancellationToken cancellationToken = default
    ) => TransactionRetry.RunAsync(() => CreateOnceAsync(bookings, cancellationToken), cancellationToken);

    private async Task<CreateResult> CreateOnceAsync(
        IReadOnlyList<Booking> bookings,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await LoadAsync(cancellationToken);
        var tenantId = snapshot.Tenant.Id;
        var batchId = bookings.Count > 1 ? Guid.NewGuid() : (Guid?)null;
        var rows = bookings.Select(b => snapshot.ToRow(b, batchId)).ToList();
        var facilityIds = rows.Select(r => r.FacilityId).Distinct().Order().ToList();

        // Disposing the transaction without committing it rolls it back, and lets go of the locks
        using var tran = sql.Ado.UseTran();
        await LockFacilitiesAsync(tenantId, facilityIds, cancellationToken);

        var existing = await FindBookingsBetweenAsync(
            tenantId,
            facilityIds,
            rows.Min(r => r.StartUtc),
            rows.Max(r => r.EndUtc),
            excluding: null,
            cancellationToken
        );
        var conflicts = BookingOverlaps.Find(bookings, existing.Select(snapshot.ToBooking).ToList());
        if (conflicts.Count > 0)
        {
            FbsMetrics.BookingClashes.Add(1);
            return new CreateResult(conflicts);
        }

        await sql.Insertable(rows).ExecuteCommandAsync(cancellationToken);
        await OutboxWriter.EnqueueAsync(
            sql,
            tenantId,
            TelegramBookingNotifier.MessageType,
            new TelegramBookingPayload
            {
                Change = BookingChange.Created,
                BookingIds = rows.Select(r => r.Id).ToList(),
                ActorMemberId = rows[0].BookedByMemberId,
            },
            cancellationToken
        );
        await CalendarOutbox.EnqueueAsync(sql, tenantId, rows.Select(r => r.Id), cancellationToken);
        tran.CommitTran();
        signal.Notify();
        FbsMetrics.BookingsMade.Add(rows.Count);

        for (var i = 0; i < bookings.Count; i++)
        {
            bookings[i].Id = rows[i].Id;
        }

        return new CreateResult([]);
    }

    public Task<UpdateResult> UpdateAsync(
        Booking updated,
        string updatedByPhone,
        bool checkForClash,
        CancellationToken cancellationToken = default
    ) => TransactionRetry.RunAsync(() => UpdateOnceAsync(updated, updatedByPhone, checkForClash, cancellationToken), cancellationToken);

    private async Task<UpdateResult> UpdateOnceAsync(
        Booking updated,
        string updatedByPhone,
        bool checkForClash,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await LoadAsync(cancellationToken);
        var tenantId = snapshot.Tenant.Id;
        var bookingId = updated.Id;
        var editor = snapshot.MemberByPhone(updatedByPhone);
        var facility = snapshot.FacilityByName(updated.FacilityName);

        // Which facilities to lock has to be known before locking, and locking has to come before the
        // booking itself is, or this and a booking being made could each be waiting for the other
        var known = await sql.Queryable<DataBooking>()
            .FirstAsync(b => b.Id == bookingId && b.TenantId == tenantId && b.CancelledAt == null, cancellationToken);
        if (known is null)
        {
            throw new KeyNotFoundException($"Booking {bookingId} does not exist.");
        }

        using var tran = sql.Ado.UseTran();
        await LockFacilitiesAsync(
            tenantId,
            new[] { known.FacilityId, facility.Id }.Distinct().Order().ToList(),
            cancellationToken
        );

        var row = await sql.Queryable<DataBooking>()
            .Where(b => b.Id == bookingId && b.TenantId == tenantId)
            .TranLock(DbLockType.Wait)
            .FirstAsync(cancellationToken);
        if (row is null || row.CancelledAt is not null || row.FacilityId != known.FacilityId)
        {
            throw new KeyNotFoundException($"Booking {bookingId} does not exist.");
        }

        var start = updated.StartDateTime?.ToUniversalTime() ?? row.StartUtc;
        var end = updated.EndDateTime?.ToUniversalTime() ?? row.EndUtc;

        // Work out here whether the time moved. The caller's copy can be stale.
        var moved = row.FacilityId != facility.Id || row.StartUtc != start || row.EndUtc != end;
        if (checkForClash || moved)
        {
            var clash = (
                await FindBookingsBetweenAsync(tenantId, [facility.Id], start, end, excluding: bookingId, cancellationToken)
            ).FirstOrDefault();
            if (clash is not null)
            {
                FbsMetrics.BookingClashes.Add(1);
                return new UpdateResult(null, clash.Id);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var previousStart = row.StartUtc;
        var previousEnd = row.EndUtc;
        var timeChanged = previousStart != start || previousEnd != end;
        row.FacilityId = facility.Id;
        row.StartUtc = start;
        row.EndUtc = end;
        row.Conduct = updated.Conduct ?? string.Empty;
        row.Description = updated.Description;
        row.PocName = updated.PocName;
        row.PocPhone = updated.PocPhone;
        row.UpdatedByMemberId = editor.Id;
        row.UpdatedAt = now;
        row.Revision++;

        // Only these, so who made the booking can't be changed by it
        await sql.Updateable(row)
            .UpdateColumns(b => new
            {
                b.FacilityId,
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
            .ExecuteCommandAsync(cancellationToken);
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
            cancellationToken
        );
        await CalendarOutbox.EnqueueAsync(sql, tenantId, [row.Id], cancellationToken);
        tran.CommitTran();
        signal.Notify();
        FbsMetrics.BookingsChanged.Add(1);

        return new UpdateResult(snapshot.ToBooking(row), null);
    }

    public async Task DeleteAsync(Guid id, string cancelledByPhone, CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadAsync(cancellationToken);
        var tenantId = snapshot.Tenant.Id;
        var cancelledById = snapshot.MemberByPhone(cancelledByPhone).Id;
        var now = DateTimeOffset.UtcNow;

        // Cancelling something already cancelled changes nothing, and tells nobody
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
            .Where(b => b.Id == id && b.TenantId == tenantId && b.CancelledAt == null)
            .ExecuteCommandAsync(cancellationToken);
        if (cancelled == 0)
        {
            return;
        }

        await OutboxWriter.EnqueueAsync(
            sql,
            tenantId,
            TelegramBookingNotifier.MessageType,
            new TelegramBookingPayload { Change = BookingChange.Cancelled, BookingIds = [id], ActorMemberId = cancelledById },
            cancellationToken
        );
        await CalendarOutbox.EnqueueAsync(sql, tenantId, [id], cancellationToken);
        tran.CommitTran();
        signal.Notify();
        FbsMetrics.BookingsCancelled.Add(1);
    }

    /// <summary>Bookings are read from the database every time, so there is nothing to reload.</summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private Task LockFacilitiesAsync(Guid tenantId, List<Guid> facilityIds, CancellationToken cancellationToken) =>
        BookingLocks.LockFacilitiesAsync(sql, tenantId, facilityIds, cancellationToken);

    private Task<List<DataBooking>> FindBookingsBetweenAsync(
        Guid tenantId,
        List<Guid> facilityIds,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? excluding,
        CancellationToken cancellationToken
    ) => BookingLocks.FindBookingsBetweenAsync(sql, tenantId, facilityIds, from, to, excluding, cancellationToken);

    private async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var current = await tenant.GetAsync(cancellationToken);
        var tenantId = current.Id;
        var facilities = await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToListAsync(cancellationToken);
        // Including those who have left: what they booked is still theirs
        var members = await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId).ToListAsync(cancellationToken);

        return new Snapshot(current, TenantTimeZone.Of(current), facilities, members);
    }

    /// <summary>What is needed to turn bookings into rows, and rows into bookings.</summary>
    private sealed class Snapshot(Tenant tenant, TimeZoneInfo zone, List<DataFacility> facilities, List<TenantMember> members)
    {
        private readonly Dictionary<Guid, DataFacility> _facilitiesById = facilities.ToDictionary(f => f.Id);
        private readonly Dictionary<string, DataFacility> _facilitiesByName = facilities.ToDictionary(f => f.Name);
        private readonly Dictionary<Guid, TenantMember> _membersById = members.ToDictionary(m => m.Id);
        private readonly Dictionary<string, TenantMember> _membersByPhone = members
            .Where(m => m.Phone is not null)
            .ToDictionary(m => m.Phone!);

        public Tenant Tenant => tenant;

        public DataFacility FacilityByName(string? name) =>
            name is not null && _facilitiesByName.TryGetValue(name, out var facility)
                ? facility
                : throw new InvalidOperationException($"Facility '{name}' does not exist.");

        public TenantMember MemberByPhone(string? phone) =>
            PhoneNumbers.ToStored(phone) is { } stored && _membersByPhone.TryGetValue(stored, out var member)
                ? member
                : throw new InvalidOperationException($"'{phone}' is not a member.");

        public DataBooking ToRow(Booking booking, Guid? batchId)
        {
            var facility = FacilityByName(booking.FacilityName);
            var member = MemberByPhone(booking.UserPhone);
            var now = DateTimeOffset.UtcNow;

            return new DataBooking
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                FacilityId = facility.Id,
                StartUtc = booking.StartDateTime!.Value.ToUniversalTime(),
                EndUtc = booking.EndDateTime!.Value.ToUniversalTime(),
                Conduct = booking.Conduct ?? string.Empty,
                Description = booking.Description,
                PocName = booking.PocName,
                PocPhone = booking.PocPhone,
                BookedByMemberId = member.Id,
                UnitId = member.UnitId,
                BatchId = batchId,
                Revision = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }

        public Booking ToBooking(DataBooking row) =>
            new()
            {
                Id = row.Id,
                FacilityName = _facilitiesById.TryGetValue(row.FacilityId, out var facility) ? facility.Name : null,
                StartDateTime = TimeZoneInfo.ConvertTime(row.StartUtc, zone),
                EndDateTime = TimeZoneInfo.ConvertTime(row.EndUtc, zone),
                Conduct = row.Conduct,
                Description = row.Description,
                PocName = row.PocName,
                PocPhone = row.PocPhone,
                UserPhone = _membersById.TryGetValue(row.BookedByMemberId, out var member) ? PhoneNumbers.ToApi(member.Phone) : null,
            };
    }
}
