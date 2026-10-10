using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Endpoints.Org.Facilities.Bookable.Get;

/// <summary>The facilities the caller can book, which is what they are asked to pick from.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : EndpointWithoutRequest<List<Response>>
{
    public override void Configure()
    {
        Get("/t/{slug}/Facilities/Bookable");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var member = tenantContext.Member;
        var facilities = await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).OrderBy(f => f.Group).OrderBy(f => f.Name).ToListAsync(ct);
        var access = (await sql.Queryable<FacilityUnitAccess>().Where(a => a.TenantId == tenantId).ToListAsync(ct)).ToLookup(a => a.FacilityId, a => a.UnitId);

        await Send.OkAsync(
            facilities
                .Where(f => BookingRules.CanBook(member, f, access[f.Id].ToHashSet()))
                .Select(f => new Response { Id = f.Id, Name = f.Name, Group = f.Group })
                .ToList(),
            ct
        );
    }
}
