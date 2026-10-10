using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Calendar.Delete;

/// <summary>Stops copying bookings to the calendar. Events already there are left.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, CalendarConnector calendars) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/t/{slug}/Calendar");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await calendars.DisconnectAsync(tenantContext.Tenant.Id, tenantContext.Member.Id, ct);
        await Send.NoContentAsync(ct);
    }
}