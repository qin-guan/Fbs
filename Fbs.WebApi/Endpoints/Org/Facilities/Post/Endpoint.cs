using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Endpoints.Org.Facilities.Post;

[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, TenantQuotas quotas, AuditLog audit) : Endpoint<Request, FacilityResponse>
{
    public override void Configure()
    {
        Post("/t/{slug}/Facilities");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        if (await quotas.CheckFacilityAsync(tenantId, ct) is { } refusal)
        {
            AddError(refusal.Reason, refusal.Code);
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var unitIds = req.UnitIds.Distinct().ToList();
        if (!await FacilityResponse.AllUnitsAreInAsync(sql, tenantId, unitIds, ct))
        {
            AddError(r => r.UnitIds, "One of the units isn't one of this organisation's.", "unit-unknown");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var facility = new DataFacility
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = req.Name.Trim(),
            Group = string.IsNullOrWhiteSpace(req.Group) ? null : req.Group.Trim(),
            AvailableToAll = req.AvailableToAll,
        };

        try
        {
            using var tran = sql.Ado.UseTran();
            await sql.Insertable(facility).ExecuteCommandAsync(ct);
            if (unitIds.Count > 0)
            {
                await sql.Insertable(unitIds.Select(unitId => new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = tenantId, FacilityId = facility.Id, UnitId = unitId }).ToList()).ExecuteCommandAsync(ct);
            }

            await audit.WriteAsync(tenantId, tenantContext.Member.Id, "facility.created", $"Added the facility {facility.Name}.", "facility", facility.Id, ct);
            tran.CommitTran();
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            AddError(r => r.Name, "There is a facility with that name already.", "facility-exists");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.ResponseAsync(FacilityResponse.From(facility, unitIds), StatusCodes.Status201Created, ct);
    }
}
