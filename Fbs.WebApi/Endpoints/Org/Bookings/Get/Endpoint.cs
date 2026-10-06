using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Bookings.Get;

/// <summary>
/// The bookings of an organisation, earliest first, cancelled ones left out. With no <c>from</c> and no <c>to</c>,
/// every one of them, which is what the list shows. With a window, those that share any time with it. A window is
/// at most <see cref="MaxDays"/> days.
/// </summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, TenantBookings bookings) : Endpoint<Request, List<BookingResponse>>
{
    public const int DefaultDays = 31;
    public const int MaxDays = 93;
    public const int MaxBookings = 2000;

    public override void Configure()
    {
        Get("/t/{slug}/Bookings");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        Guid? bookedBy = req.Mine ? tenantContext.Member.Id : req.BookedBy;

        // The list asks for everything. The timeline, and picking a time, ask for a window.
        if (req.From is null && req.To is null)
        {
            var all = await bookings.ListAllAsync(tenant.Id, req.FacilityId, bookedBy, ct);
            await Send.OkAsync(await BookingViews.ToResponsesAsync(sql, tenantContext, all, ct), ct);
            return;
        }

        var from = req.From ?? req.To!.Value.AddDays(-DefaultDays);
        var to = req.To ?? from.AddDays(DefaultDays);
        if (to <= from)
        {
            AddError(r => r.To!, "The end of the window has to be after its start.", "window-invalid");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (to - from > TimeSpan.FromDays(MaxDays))
        {
            AddError(r => r.To!, $"A window can be up to {MaxDays} days.", "window-too-large");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var rows = await bookings.ListAsync(tenant.Id, from, to, req.FacilityId, bookedBy, MaxBookings + 1, ct);
        if (rows.Count > MaxBookings)
        {
            AddError(r => r.To!, "There are too many bookings in that window to show at once, so ask for a shorter one.", "window-too-large");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await Send.OkAsync(await BookingViews.ToResponsesAsync(sql, tenantContext, rows, ct), ct);
    }
}
