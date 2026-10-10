using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Calendar.Confirm.Post;

/// <summary>Starts copying, once the code written into the calendar is given back.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, CalendarConnector calendars) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/t/{slug}/Calendar/Confirm");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var attempt = await calendars.ConfirmAsync(tenantContext.Tenant.Id, tenantContext.Member.Id, req.Code, ct);
        if (attempt.Refusal is { } refusal)
        {
            if (refusal.Field == "code")
            {
                AddError(r => r.Code, refusal.Message, refusal.Code);
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