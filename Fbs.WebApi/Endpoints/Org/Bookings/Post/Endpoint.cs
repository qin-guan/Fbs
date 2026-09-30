using FastEndpoints;
using FluentValidation.Results;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Endpoints.Org.Bookings.Post;

/// <summary>Books one or several slots as the caller.</summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, TenantBookings bookings, TenantQuotas quotas) : Endpoint<Request, List<BookingResponse>>
{
    public override void Configure()
    {
        Post("/t/{slug}/Bookings");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        var member = tenantContext.Member;
        var zone = TenantTimeZone.Of(tenant);
        var now = DateTimeOffset.UtcNow;

        var facilities = (await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenant.Id).ToListAsync(ct)).ToDictionary(f => f.Id);
        var access = (await sql.Queryable<FacilityUnitAccess>().Where(a => a.TenantId == tenant.Id).ToListAsync(ct)).ToLookup(a => a.FacilityId, a => a.UnitId);

        for (var i = 0; i < req.Slots.Count; i++)
        {
            var slot = req.Slots[i];
            if (!facilities.TryGetValue(slot.FacilityId, out var facility))
            {
                AddSlotError(i, "There is no such facility.", "facility-unknown");
            }
            else if (!BookingRules.CanBook(member, facility, access[facility.Id].ToHashSet()))
            {
                AddSlotError(i, $"You do not have permission to book {facility.Name}.", "facility-forbidden");
            }

            if (slot.StartDateTime < now)
            {
                AddSlotError(i, "The start has to be in the future.", "start-past");
            }

            if (slot.EndDateTime <= slot.StartDateTime)
            {
                AddSlotError(i, "The end has to be after the start.", "end-before-start");
            }

            if (!BookingRules.IsOnSlot(slot.StartDateTime, zone, tenant.SlotMinutes) || !BookingRules.IsOnSlot(slot.EndDateTime, zone, tenant.SlotMinutes))
            {
                AddSlotError(i, $"Times have to be on the {tenant.SlotMinutes} minutes.", "not-on-slot");
            }
        }

        ThrowIfAnyErrors();

        if (await quotas.CheckBookingsAsync(tenant.Id, req.Slots.Count, ct) is { } refusal)
        {
            AddError(refusal.Reason, refusal.Code);
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var details = new BookingDetails(req.Conduct!.Trim(), Clean(req.Description), Clean(req.PocName), Clean(req.PocPhone));
        var slots = req.Slots.Select(s => new NewSlot(s.FacilityId, s.StartDateTime, s.EndDateTime)).ToList();
        var result = await bookings.CreateAsync(tenant.Id, member, details, slots, ct);
        if (!result.Succeeded)
        {
            foreach (var conflict in result.Conflicts)
            {
                AddSlotError(
                    conflict.Index,
                    conflict.WithBookingId is { } existing ? $"Overlaps with booking {existing}." : $"Overlaps with slot {conflict.WithEarlierIndex + 1} in this request.",
                    "clash"
                );
            }

            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.ResponseAsync(await BookingViews.ToResponsesAsync(sql, tenantContext, result.Created, ct), StatusCodes.Status201Created, ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void AddSlotError(int index, string message, string code) => AddError(new ValidationFailure($"slots[{index}]", message) { ErrorCode = code });
}
