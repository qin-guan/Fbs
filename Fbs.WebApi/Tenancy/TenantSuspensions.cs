using Fbs.WebApi.Data.Entities;
using SqlSugar;

namespace Fbs.WebApi.Tenancy;

public enum SuspensionOutcome
{
    /// <summary>It was active and is suspended now, or was suspended and is active now.</summary>
    Changed = 1,

    /// <summary>It was that way already, so nothing was done.</summary>
    AlreadyThatWay = 2,

    NoSuchOrganization = 3,

    /// <summary>It is to be deleted, which is more than suspended: restore it first.</summary>
    PendingDeletion = 4,
}

/// <summary>What is known of an organisation, for whoever runs the system to choose which to suspend.</summary>
public sealed record TenantSummary(string Slug, string Name, TenantStatus Status, int People, int BookingsInLastDay, DateTimeOffset CreatedAt);

/// <summary>
/// Stopping an organisation being used, and starting it again. This is for whoever runs the system, when one is being used
/// to abuse it: nobody in a suspended organisation can use it, its admins included, and nothing is deleted.
/// </summary>
public sealed class TenantSuspensions(ISqlSugarClient sql)
{
    public async Task<SuspensionOutcome> SetAsync(string tenantSlug, bool suspended, CancellationToken ct)
    {
        var tenant = await sql.Queryable<Tenant>().FirstAsync(t => t.Slug == tenantSlug, ct);
        if (tenant is null)
        {
            return SuspensionOutcome.NoSuchOrganization;
        }

        if (tenant.Status == TenantStatus.PendingDeletion)
        {
            return SuspensionOutcome.PendingDeletion;
        }

        var wanted = suspended ? TenantStatus.Suspended : TenantStatus.Active;
        if (tenant.Status == wanted)
        {
            return SuspensionOutcome.AlreadyThatWay;
        }

        var id = tenant.Id;
        await sql.Updateable<Tenant>().SetColumns(t => new Tenant { Status = wanted }).Where(t => t.Id == id).ExecuteCommandAsync(ct);
        await new AuditLog(sql).WriteAsync(
            id,
            null,
            suspended ? "tenant.suspended" : "tenant.unsuspended",
            suspended ? "Suspended by whoever runs the system." : "Made available again by whoever runs the system.",
            "tenant",
            id,
            ct
        );
        return SuspensionOutcome.Changed;
    }

    /// <summary>Every organisation, the newest first, with how many people it has and how much it has been used.</summary>
    public async Task<List<TenantSummary>> ListAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var removed = MemberStatus.Removed;
        var tenants = await sql.Queryable<Tenant>().OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        var people = (await sql.Queryable<TenantMember>().Where(m => m.Status != removed).GroupBy(m => m.TenantId).Select(m => new { m.TenantId, Count = SqlFunc.AggregateCount(m.Id) }).ToListAsync(ct))
            .ToDictionary(x => x.TenantId, x => x.Count);
        var bookings = (await sql.Queryable<Booking>().Where(b => b.CreatedAt > since).GroupBy(b => b.TenantId).Select(b => new { b.TenantId, Count = SqlFunc.AggregateCount(b.Id) }).ToListAsync(ct))
            .ToDictionary(x => x.TenantId, x => x.Count);

        return tenants.Select(t => new TenantSummary(t.Slug, t.Name, t.Status, people.GetValueOrDefault(t.Id), bookings.GetValueOrDefault(t.Id), t.CreatedAt)).ToList();
    }
}
