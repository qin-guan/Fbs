using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Settings.Get;

public class Response
{
    public required string Name { get; init; }

    public required string TimeZone { get; init; }

    public required string DefaultCountryCode { get; init; }

    public required int SlotMinutes { get; init; }

    /// <summary>Whether someone who joins with an invite waits for an admin to let them in.</summary>
    public required bool RequireApproval { get; init; }
}

[RequiresClerk]
public class Endpoint(ITenantContext tenantContext) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("Settings");
        Group<TenantAdminGroup>();
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
            },
            ct
        );
    }
}
