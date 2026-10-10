using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Bookings.ById.Delete;

/// <summary>Cancels a booking, which is kept. Its own booker, anyone in their unit and admins can.</summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, TenantBookings bookings) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/t/{slug}/Bookings/{id}");
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
            AddError("You can only cancel bookings made by you or your unit.", "not-yours");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        if (!await bookings.CancelAsync(tenant.Id, member, row.Id, ct))
        {
            // Somebody else cancelled it in the meantime
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
