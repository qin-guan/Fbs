using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Bookings.ById.Put;

/// <summary>
/// Changes what a booking says, and when it is. Whoever made it never changes, and it stays at the facility it was made
/// for. Its own booker, anyone in their unit and admins can change it.
/// </summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, TenantBookings bookings) : Endpoint<Request, BookingResponse>
{
    public override void Configure()
    {
        Put("/t/{slug}/Bookings/{id}");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        var member = tenantContext.Member;
        var row = await bookings.FindAsync(tenant.Id, req.Id, ct);
        if (row is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var bookedById = row.BookedByMemberId;
        var bookedBy = await sql.Queryable<TenantMember>().FirstAsync(m => m.Id == bookedById && m.TenantId == tenant.Id, ct);
        if (!BookingRules.CanManage(member, bookedBy))
        {
            AddError("You can only change bookings made by you or your unit.", "not-yours");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var start = req.StartDateTime?.ToUniversalTime();
        var end = req.EndDateTime?.ToUniversalTime();
        if ((start ?? row.StartUtc) != row.StartUtc || (end ?? row.EndUtc) != row.EndUtc)
        {
            ValidateTimeChange(row.StartUtc, row.EndUtc, start!.Value, end!.Value, TenantTimeZone.Of(tenant), tenant.SlotMinutes);
            ThrowIfAnyErrors();
        }

        var details = new BookingDetails(req.Conduct!.Trim(), Clean(req.Description), Clean(req.PocName), Clean(req.PocPhone));
        var result = await bookings.UpdateAsync(tenant.Id, member, row.Id, details, start, end, ct);
        if (result.NotFound)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (result.Updated is not { } saved)
        {
            AddError(r => r.EndDateTime!, $"Overlaps with booking {result.ClashesWith}.", "clash");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.OkAsync((await BookingViews.ToResponsesAsync(sql, tenantContext, [saved], ct)).Single(), ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Bookings that are over can't be moved. One that hasn't started can move anywhere in the future; one that is
    /// underway keeps its start and can only have its end changed.
    /// </summary>
    private void ValidateTimeChange(DateTimeOffset previousStart, DateTimeOffset previousEnd, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, int slotMinutes)
    {
        var now = DateTimeOffset.UtcNow;
        if (previousEnd <= now)
        {
            AddError(r => r.StartDateTime!, "This booking is over, so its time can no longer be changed.", "over");
            return;
        }

        if (end <= start)
        {
            AddError(r => r.EndDateTime!, "The end has to be after the start.", "end-before-start");
        }

        if (start != previousStart)
        {
            if (previousStart <= now)
            {
                AddError(r => r.StartDateTime!, "This booking has started, so only its end can be changed.", "started");
            }
            else if (start < now)
            {
                AddError(r => r.StartDateTime!, "The start has to be in the future.", "start-past");
            }
        }

        if (end <= now)
        {
            AddError(r => r.EndDateTime!, "The end has to be in the future.", "end-past");
        }

        if (!BookingRules.IsOnSlot(start, zone, slotMinutes) || !BookingRules.IsOnSlot(end, zone, slotMinutes))
        {
            AddError(r => r.StartDateTime!, $"Times have to be on the {slotMinutes} minutes.", "not-on-slot");
        }
    }
}
