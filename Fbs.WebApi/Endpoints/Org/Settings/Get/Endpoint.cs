using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Settings.Get;

[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/t/{slug}/Settings");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        await Send.OkAsync(
            new Response
            {
                Name = tenant.Name,
                TimeZone = tenant.TimeZone,
                DefaultCountryCode = tenant.DefaultCountryCode,
                SlotMinutes = tenant.SlotMinutes,
                RequireApproval = tenant.RequireApproval,
                LegacyClaimEnabled = tenant.LegacyClaimEnabled,
            },
            ct
        );
    }
}
