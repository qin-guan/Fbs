using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Endpoints.Org.Units.Get;

/// <summary>The units of the organisation, which every member can see, as they are what people are asked to pick from.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : EndpointWithoutRequest<List<UnitResponse>>
{
    public override void Configure()
    {
        Get("/t/{slug}/Units");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var units = await sql.Queryable<DataUnit>().Where(u => u.TenantId == tenantId).OrderBy(u => u.Name).ToListAsync(ct);
        await Send.OkAsync(units.Select(UnitResponse.From).ToList(), ct);
    }
}
