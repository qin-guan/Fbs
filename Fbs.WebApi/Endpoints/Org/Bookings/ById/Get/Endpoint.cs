using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Bookings.ById.Get;

[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, TenantBookings bookings) : Endpoint<Request, BookingResponse>
{
    public override void Configure()
    {
        Get("/t/{slug}/Bookings/{id}");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var row = await bookings.FindAsync(tenantContext.Tenant.Id, req.Id, ct);
        if (row is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync((await BookingViews.ToResponsesAsync(sql, tenantContext, [row], ct)).Single(), ct);
    }
}
