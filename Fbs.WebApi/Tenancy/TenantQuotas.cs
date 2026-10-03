using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Telemetry;
using Microsoft.Extensions.Options;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tenancy;

/// <summary>Why something can't be made: what to tell whoever asked, and a code that says which limit it is.</summary>
public sealed record QuotaRefusal(string Reason, string Code);

/// <summary>
/// What an organisation can have, and make in a day, so that one can't use up what all of them share. Each is looked at just
/// before what it is about is made, so that a number of them made at the same moment can go a little over: it is to stop
/// the database being filled, and not to be exact.
/// </summary>
public sealed class TenantQuotas(ISqlSugarClient sql, IOptions<TenantLimits> options)
{
    private readonly TenantLimits _limits = options.Value;

    public async Task<QuotaRefusal?> CheckUnitAsync(Guid tenantId, CancellationToken ct)
    {
        var count = await sql.Queryable<DataUnit>().CountAsync(u => u.TenantId == tenantId, ct);
        return count >= _limits.MaxUnits ? Refused($"An organisation can have {_limits.MaxUnits} units. Delete one that isn't needed.", "unit-limit") : null;
    }

    public async Task<QuotaRefusal?> CheckFacilityAsync(Guid tenantId, CancellationToken ct)
    {
        var count = await sql.Queryable<DataFacility>().CountAsync(f => f.TenantId == tenantId, ct);
        return count >= _limits.MaxFacilities ? Refused($"An organisation can have {_limits.MaxFacilities} facilities. Delete one that isn't needed.", "facility-limit") : null;
    }

    /// <summary>Whether one more person can be in: somebody who was removed and is being let back in is one more.</summary>
    public async Task<QuotaRefusal?> CheckMemberAsync(Guid tenantId, CancellationToken ct)
    {
        var removed = MemberStatus.Removed;
        var count = await sql.Queryable<TenantMember>().CountAsync(m => m.TenantId == tenantId && m.Status != removed, ct);
        return count >= _limits.MaxMembers ? Refused($"An organisation can have {_limits.MaxMembers} people. Remove somebody who isn't needed.", "member-limit") : null;
    }

    /// <summary>Whether <paramref name="adding"/> more bookings can be made, on top of what has been in the last 24 hours.</summary>
    public async Task<QuotaRefusal?> CheckBookingsAsync(Guid tenantId, int adding, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var made = await sql.Queryable<Booking>().CountAsync(b => b.TenantId == tenantId && b.CreatedAt > since, ct);
        return made + adding > _limits.MaxBookingsPerDay
            ? Refused($"An organisation can make {_limits.MaxBookingsPerDay} bookings in a day, and this one has made {made} in the last 24 hours. Try again later.", "booking-limit")
            : null;
    }

    /// <summary>The refusal, counted by which limit it was: somebody at a limit is somebody to talk to, or to stop.</summary>
    private static QuotaRefusal Refused(string reason, string code)
    {
        FbsMetrics.QuotaRefusals.Add(1, new KeyValuePair<string, object?>("limit", code));
        return new QuotaRefusal(reason, code);
    }
}
