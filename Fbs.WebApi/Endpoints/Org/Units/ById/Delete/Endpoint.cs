using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Endpoints.Org.Units.ById.Delete;

/// <summary>
/// Takes a unit away, unless people are in it or bookings were made for it, as those would lose theirs. Members who
/// have left don't count: they lose it, and the facilities it could book are no longer available to it.
/// </summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, AuditLog audit) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/t/{slug}/Units/{id}");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var id = req.Id;
        var removed = MemberStatus.Removed;

        using var tran = sql.Ado.UseTran();
        var unit = await sql.Queryable<DataUnit>().Where(u => u.Id == id && u.TenantId == tenantId).TranLock(DbLockType.Wait).FirstAsync(ct);
        if (unit is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var members = await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId && m.UnitId == id && m.Status != removed).CountAsync(ct);
        var bookings = await sql.Queryable<DataBooking>().Where(b => b.TenantId == tenantId && b.UnitId == id).CountAsync(ct);
        if (members > 0 || bookings > 0)
        {
            AddError("Move the people in it to another unit first, and a unit that bookings were made for can't be deleted.", "unit-in-use");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await sql.Updateable<TenantMember>().SetColumns(m => new TenantMember { UnitId = null }).Where(m => m.TenantId == tenantId && m.UnitId == id).ExecuteCommandAsync(ct);
        await sql.Deleteable<FacilityUnitAccess>().Where(a => a.TenantId == tenantId && a.UnitId == id).ExecuteCommandAsync(ct);
        await sql.Deleteable<DataUnit>().Where(u => u.Id == id && u.TenantId == tenantId).ExecuteCommandAsync(ct);
        await audit.WriteAsync(tenantId, tenantContext.Member.Id, "unit.deleted", $"Deleted the unit {unit.Name}.", "unit", id, ct);
        tran.CommitTran();

        await Send.NoContentAsync(ct);
    }
}
