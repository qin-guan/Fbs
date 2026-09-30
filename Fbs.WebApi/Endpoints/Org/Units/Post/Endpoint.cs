using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Endpoints.Org.Units.Post;

[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, TenantQuotas quotas) : Endpoint<Request, UnitResponse>
{
    public override void Configure()
    {
        Post("/t/{slug}/Units");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        if (await quotas.CheckUnitAsync(tenantContext.Tenant.Id, ct) is { } refusal)
        {
            AddError(refusal.Reason, refusal.Code);
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var unit = new DataUnit { Id = Guid.NewGuid(), TenantId = tenantContext.Tenant.Id, Name = req.Name.Trim() };
        try
        {
            await sql.Insertable(unit).ExecuteCommandAsync(ct);
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            AddError(r => r.Name, "There is a unit with that name already.", "unit-exists");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.ResponseAsync(UnitResponse.From(unit), StatusCodes.Status201Created, ct);
    }
}
