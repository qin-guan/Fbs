using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Endpoints.Org.Facilities.ById.Put;

/// <summary>Replaces what a facility is called, how it is grouped and who can book it.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, AuditLog audit) : Endpoint<Request, FacilityResponse>
{
    public override void Configure()
    {
        Put("/t/{slug}/Facilities/{id}");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var id = req.Id;
        var unitIds = req.UnitIds.Distinct().ToList();
        var name = req.Name.Trim();
        var group = string.IsNullOrWhiteSpace(req.Group) ? null : req.Group.Trim();
        var availableToAll = req.AvailableToAll;

        if (!await FacilityResponse.AllUnitsAreInAsync(sql, tenantId, unitIds, ct))
        {
            AddError(r => r.UnitIds, "One of the units isn't one of this organisation's.", "unit-unknown");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        try
        {
            using var tran = sql.Ado.UseTran();
            // Locked like it is when somebody books it, so a change and a booking are one after the other
            var facility = await sql.Queryable<DataFacility>().Where(f => f.Id == id && f.TenantId == tenantId).TranLock(DbLockType.Wait).FirstAsync(ct);
            if (facility is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await sql.Updateable<DataFacility>()
                .SetColumns(f => new DataFacility { Name = name, Group = group, AvailableToAll = availableToAll })
                .Where(f => f.Id == id && f.TenantId == tenantId)
                .ExecuteCommandAsync(ct);
            await sql.Deleteable<FacilityUnitAccess>().Where(a => a.FacilityId == id && a.TenantId == tenantId).ExecuteCommandAsync(ct);
            if (unitIds.Count > 0)
            {
                await sql.Insertable(unitIds.Select(unitId => new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = tenantId, FacilityId = id, UnitId = unitId }).ToList()).ExecuteCommandAsync(ct);
            }

            await audit.WriteAsync(
                tenantId,
                tenantContext.Member.Id,
                "facility.changed",
                facility.Name == name ? $"Changed the facility {name}." : $"Changed the facility {facility.Name}, which is now {name}.",
                "facility",
                id,
                ct
            );
            tran.CommitTran();
            facility.Name = name;
            facility.Group = group;
            facility.AvailableToAll = availableToAll;
            await Send.OkAsync(FacilityResponse.From(facility, unitIds), ct);
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            AddError(r => r.Name, "There is a facility with that name already.", "facility-exists");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
        }
    }
}
