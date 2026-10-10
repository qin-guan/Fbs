using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Calendar.Get;

/// <summary>The organization's Google Calendar, if it has one, and the account a calendar has to be shared with.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, CalendarConnector calendars) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/t/{slug}/Calendar");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var (connection, email) = await calendars.DescribeAsync(tenantContext.Tenant.Id, ct);
        await Send.OkAsync(Response.From(connection, email), ct);
    }
}