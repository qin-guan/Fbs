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
        var claimEnabled = req.LegacyClaimEnabled ?? tenantContext.Tenant.LegacyClaimEnabled;
        if (claimEnabled && !tenantContext.Tenant.LegacyClaimEnabled)
        {
            AddError(r => r.LegacyClaimEnabled!, "Claiming places can be turned off, and not on again.", "claim-cannot-enable");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

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
                LegacyClaimEnabled = claimEnabled,
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
                LegacyClaimEnabled = claimEnabled,
            },
            ct
        );
    }
}
