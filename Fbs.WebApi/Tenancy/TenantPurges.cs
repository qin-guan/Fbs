using Fbs.WebApi.Data.Entities;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tenancy;

public enum PurgeOutcome
{
    /// <summary>It, and everything of it, is deleted.</summary>
    Purged = 1,

    NotFound = 2,

    /// <summary>It isn't to be deleted, or it is but not yet: nothing was done.</summary>
    NotDue = 3,
}

/// <summary>What was deleted, by what it was, or would have been if this was a dry run.</summary>
public sealed record PurgeResult(string Slug, PurgeOutcome Outcome, IReadOnlyDictionary<string, int> Deleted)
{
    public int Total => Deleted.Values.Sum();
}

/// <summary>
/// Deleting an organisation for good, and everything of it, once it has been asked for and the time to change one's mind has
/// passed. It is for whoever runs the system, from the migrator, on a schedule. People stay: their accounts, and the chats
/// they connected, are theirs and not the organisation's. What was copied to a Google Calendar stays there, as that
/// is the organisation's own.
/// </summary>
public sealed class TenantPurges(ISqlSugarClient sql)
{
    /// <summary>
    /// What belongs to an organisation, and is deleted with it. Everything with a <c>TenantId</c> is here, and a test says so: the
    /// one that is added and left out would be kept for good after the organisation was deleted.
    /// </summary>
    public static readonly IReadOnlyList<Type> TenantOwned =
    [
        typeof(AuditEntry),
        typeof(BookingCalendarEvent),
        typeof(Booking),
        typeof(CalendarConnection),
        typeof(FacilityUnitAccess),
        typeof(DataFacility),
        typeof(MemberClaimToken),
        typeof(OutboxMessage),
        typeof(RosterEntry),
        typeof(TenantInvite),
        typeof(TenantMember),
        typeof(DataUnit),
    ];

    /// <summary>The organisations that were asked to be deleted, and can be now.</summary>
    public async Task<List<string>> DueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var pending = TenantStatus.PendingDeletion;
        return (await sql.Queryable<Tenant>().Where(t => t.Status == pending && t.DeleteAfter != null && t.DeleteAfter <= now).OrderBy(t => t.DeleteAfter).ToListAsync(ct)).Select(t => t.Slug).ToList();
    }

    /// <param name="early">Whether it can be before the time is up: for when somebody has to be erased sooner, and an admin has asked for it to be deleted.</param>
    /// <param name="dryRun">Only say what would be deleted.</param>
    public async Task<PurgeResult> PurgeAsync(string slug, DateTimeOffset now, bool early, bool dryRun, CancellationToken ct)
    {
        using var tran = sql.Ado.UseTran();

        // Locked, and looked at again in the lock: an admin restoring it at this moment either did before, and it stays, or is told there is nothing to restore
        var tenant = await sql.Queryable<Tenant>().Where(t => t.Slug == slug).TranLock(DbLockType.Wait).FirstAsync(ct);
        if (tenant is null)
        {
            return new PurgeResult(slug, PurgeOutcome.NotFound, new Dictionary<string, int>());
        }

        if (tenant.Status != TenantStatus.PendingDeletion || (!early && (tenant.DeleteAfter is null || tenant.DeleteAfter > now)))
        {
            return new PurgeResult(slug, PurgeOutcome.NotDue, new Dictionary<string, int>());
        }

        var id = tenant.Id;
        var deleted = new Dictionary<string, int>
        {
            [nameof(AuditEntry)] = await Run<AuditEntry>(q => q.Where(e => e.TenantId == id), d => d.Where(e => e.TenantId == id), dryRun, ct),
            [nameof(BookingCalendarEvent)] = await Run<BookingCalendarEvent>(q => q.Where(e => e.TenantId == id), d => d.Where(e => e.TenantId == id), dryRun, ct),
            [nameof(Booking)] = await Run<Booking>(q => q.Where(b => b.TenantId == id), d => d.Where(b => b.TenantId == id), dryRun, ct),
            [nameof(CalendarConnection)] = await Run<CalendarConnection>(q => q.Where(c => c.TenantId == id), d => d.Where(c => c.TenantId == id), dryRun, ct),
            [nameof(FacilityUnitAccess)] = await Run<FacilityUnitAccess>(q => q.Where(a => a.TenantId == id), d => d.Where(a => a.TenantId == id), dryRun, ct),
            [nameof(DataFacility)] = await Run<DataFacility>(q => q.Where(f => f.TenantId == id), d => d.Where(f => f.TenantId == id), dryRun, ct),
            [nameof(MemberClaimToken)] = await Run<MemberClaimToken>(q => q.Where(c => c.TenantId == id), d => d.Where(c => c.TenantId == id), dryRun, ct),
            [nameof(OutboxMessage)] = await Run<OutboxMessage>(q => q.Where(m => m.TenantId == id), d => d.Where(m => m.TenantId == id), dryRun, ct),
            [nameof(RosterEntry)] = await Run<RosterEntry>(q => q.Where(r => r.TenantId == id), d => d.Where(r => r.TenantId == id), dryRun, ct),
            [nameof(TenantInvite)] = await Run<TenantInvite>(q => q.Where(i => i.TenantId == id), d => d.Where(i => i.TenantId == id), dryRun, ct),
            [nameof(TenantMember)] = await Run<TenantMember>(q => q.Where(m => m.TenantId == id), d => d.Where(m => m.TenantId == id), dryRun, ct),
            [nameof(DataUnit)] = await Run<DataUnit>(q => q.Where(u => u.TenantId == id), d => d.Where(u => u.TenantId == id), dryRun, ct),
        };

        // The organisation itself goes last
        deleted[nameof(Tenant)] = dryRun ? 1 : await sql.Deleteable<Tenant>().Where(t => t.Id == id).ExecuteCommandAsync(ct);

        if (!dryRun)
        {
            tran.CommitTran();
        }

        return new PurgeResult(slug, PurgeOutcome.Purged, deleted);
    }

    private async Task<int> Run<T>(Func<ISugarQueryable<T>, ISugarQueryable<T>> count, Func<IDeleteable<T>, IDeleteable<T>> delete, bool dryRun, CancellationToken ct)
        where T : class, new() =>
        dryRun ? await count(sql.Queryable<T>()).CountAsync(ct) : await delete(sql.Deleteable<T>()).ExecuteCommandAsync(ct);
}
