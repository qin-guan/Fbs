using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Endpoints.Org.Units.ById.Put;

[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : Endpoint<Request, UnitResponse>
{
    public override void Configure()
    {
        Put("/t/{slug}/Units/{id}");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var id = req.Id;
        var name = req.Name.Trim();
        var unit = await sql.Queryable<DataUnit>().FirstAsync(u => u.Id == id && u.TenantId == tenantId, ct);
        if (unit is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            await sql.Updateable<DataUnit>().SetColumns(u => new DataUnit { Name = name }).Where(u => u.Id == id && u.TenantId == tenantId).ExecuteCommandAsync(ct);
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            AddError(r => r.Name, "There is a unit with that name already.", "unit-exists");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        unit.Name = name;
        await Send.OkAsync(UnitResponse.From(unit), ct);
    }
}
