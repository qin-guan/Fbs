using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Endpoints.Org.Facilities.ById.Delete;

/// <summary>Takes a facility away, unless anything was ever booked on it, as its bookings would have nothing to be for.</summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, AuditLog audit) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/t/{slug}/Facilities/{id}");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var id = req.Id;

        using var tran = sql.Ado.UseTran();
        // Locked the way bookings lock it, so that one made at the moment it is deleted is either seen here or finds it gone
        var facility = await sql.Queryable<DataFacility>().Where(f => f.Id == id && f.TenantId == tenantId).TranLock(DbLockType.Wait).FirstAsync(ct);
        if (facility is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var booked = (await sql.Queryable<DataBooking>().Where(b => b.TenantId == tenantId && b.FacilityId == id).Select(b => b.Id).TranLock(DbLockType.Wait).Take(1).ToListAsync(ct)).Count;
        if (booked > 0)
        {
            AddError("Something has been booked on it, so it can't be deleted.", "facility-in-use");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await sql.Deleteable<FacilityUnitAccess>().Where(a => a.FacilityId == id && a.TenantId == tenantId).ExecuteCommandAsync(ct);
        await sql.Deleteable<DataFacility>().Where(f => f.Id == id && f.TenantId == tenantId).ExecuteCommandAsync(ct);
        await audit.WriteAsync(tenantId, tenantContext.Member.Id, "facility.deleted", $"Deleted the facility {facility.Name}.", "facility", id, ct);
        tran.CommitTran();

        await Send.NoContentAsync(ct);
    }
}
