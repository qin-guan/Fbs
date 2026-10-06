using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Calendar.Post;

/// <summary>
/// Asks to copy bookings to a calendar. A code is written into it, and copying starts when that code is given
/// back. The code is not in the answer: it has to be read from the calendar.
/// </summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, CalendarConnector calendars) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/t/{slug}/Calendar");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var attempt = await calendars.StartAsync(tenantContext.Tenant.Id, tenantContext.Member.Id, req.CalendarId, ct);
        if (attempt.Refusal is { } refusal)
        {
            if (refusal.Field == "calendarId")
            {
                AddError(r => r.CalendarId, refusal.Message, refusal.Code);
            }
            else
            {
                AddError(refusal.Message, refusal.Code);
            }

            await Send.ErrorsAsync(refusal.Status, ct);
            return;
        }

        await Send.OkAsync(Response.From(attempt.Connection, calendars.ServiceAccountEmail()), ct);
    }
}