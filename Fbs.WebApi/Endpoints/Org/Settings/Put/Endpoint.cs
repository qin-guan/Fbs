using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Settings.Put;

[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, AuditLog audit) : Endpoint<Request, Get.Response>
{
    public override void Configure()
    {
        Put("/t/{slug}/Settings");
        AuthSchemes(AccountAuthentication.Scheme);
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

        var before = tenantContext.Tenant;
        var changes = new (bool Changed, string What)[]
        {
            (name != before.Name, "name"),
            (timeZone != before.TimeZone, "time zone"),
            (countryCode != before.DefaultCountryCode, "calling code"),
            (req.SlotMinutes != before.SlotMinutes, "shortest booking"),
            (req.RequireApproval != before.RequireApproval, "whether people who join wait to be let in"),
            (claimEnabled != before.LegacyClaimEnabled, "claiming places from the previous version"),
        }
            .Where(c => c.Changed)
            .Select(c => c.What)
            .ToList();
        if (changes.Count > 0)
        {
            await audit.WriteAsync(id, tenantContext.Member.Id, "settings.changed", $"Changed the settings: {string.Join(", ", changes)}.", "tenant", id, ct);
        }

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
