using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Bookings.Get;

/// <summary>
/// The bookings that share any time with a window, earliest first. A window is at most <see cref="Endpoint.MaxDays"/>
/// days, as an organisation can have thousands of bookings and nobody looks at all of them at once.
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
        var from = req.From ?? (req.To is { } to0 ? to0.AddDays(-DefaultDays) : StartOfToday(tenant));
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

        Guid? bookedBy = req.Mine ? tenantContext.Member.Id : req.BookedBy;
        var rows = await bookings.ListAsync(tenant.Id, from, to, req.FacilityId, bookedBy, MaxBookings + 1, ct);
        if (rows.Count > MaxBookings)
        {
            AddError(r => r.To!, "There are too many bookings in that window to show at once, so ask for a shorter one.", "window-too-large");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await Send.OkAsync(await BookingViews.ToResponsesAsync(sql, tenantContext, rows, ct), ct);
    }

    private static DateTimeOffset StartOfToday(Data.Entities.Tenant tenant)
    {
        var zone = TenantTimeZone.Of(tenant);
        var now = DateTimeOffset.UtcNow;
        try
        {
            var midnight = TimeZoneInfo.ConvertTime(now, zone).Date;
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone), TimeSpan.Zero);
        }
        catch (ArgumentException)
        {
            // A zone whose day doesn't begin at midnight, such as when the clocks go forward then
            return new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        }
    }
}
