using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Get;

/// <summary>The organisation, and who the caller is in it.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/t/{slug}");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        var member = tenantContext.Member;
        await Send.OkAsync(
            new Response
            {
                Slug = tenant.Slug,
                Name = tenant.Name,
                TimeZone = tenant.TimeZone,
                DefaultCountryCode = tenant.DefaultCountryCode,
                SlotMinutes = tenant.SlotMinutes,
                Me = new Me
                {
                    MemberId = member.Id,
                    DisplayName = member.DisplayName,
                    Role = member.Role,
                    NotificationScope = member.NotificationScope,
                    Phone = member.Phone,
                },
            },
            ct
        );
    }
}
