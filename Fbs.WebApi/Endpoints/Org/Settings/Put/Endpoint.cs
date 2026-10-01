using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Settings.Put;

[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : Endpoint<Request, Get.Response>
{
    public override void Configure()
    {
        Put("/t/{slug}/Settings");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var id = tenantContext.Tenant.Id;
        var name = req.Name.Trim();
        var timeZone = req.TimeZone.Trim();
        var countryCode = req.DefaultCountryCode.Trim();
        await sql.Updateable<Tenant>()
            .SetColumns(t => new Tenant
            {
                Name = name,
                TimeZone = timeZone,
                DefaultCountryCode = countryCode,
                SlotMinutes = req.SlotMinutes,
                RequireApproval = req.RequireApproval,
            })
            .Where(t => t.Id == id)
            .ExecuteCommandAsync(ct);

        await Send.OkAsync(
            new Get.Response
            {
                Name = name,
                TimeZone = timeZone,
                DefaultCountryCode = countryCode,
                SlotMinutes = req.SlotMinutes,
                RequireApproval = req.RequireApproval,
            },
            ct
        );
    }
}
