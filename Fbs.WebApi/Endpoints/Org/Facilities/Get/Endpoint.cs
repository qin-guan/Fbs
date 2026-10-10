using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Endpoints.Org.Facilities.Get;

/// <summary>Every facility of the organisation and who can book it, for its admins to manage.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : EndpointWithoutRequest<List<FacilityResponse>>
{
    public override void Configure()
    {
        Get("/t/{slug}/Facilities");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var facilities = await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).OrderBy(f => f.Name).ToListAsync(ct);
        var access = (await sql.Queryable<FacilityUnitAccess>().Where(a => a.TenantId == tenantId).ToListAsync(ct))
            .ToLookup(a => a.FacilityId, a => a.UnitId);

        await Send.OkAsync(facilities.Select(f => FacilityResponse.From(f, access[f.Id])).ToList(), ct);
    }
}
