using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Endpoints.Org.Bookings;

public class BookingResponse
{
    public required Guid Id { get; init; }

    public required Guid FacilityId { get; init; }

    public string? FacilityName { get; init; }

    /// <summary>In the organisation's time zone.</summary>
    public required DateTimeOffset StartDateTime { get; init; }

    public required DateTimeOffset EndDateTime { get; init; }

    public required string Conduct { get; init; }

    public string? Description { get; init; }

    /// <summary>The point of contact, as it was written when the booking was made or last changed.</summary>
    public string? PocName { get; init; }

    public string? PocPhone { get; init; }

    /// <summary>Who made it, which never changes.</summary>
    public required Person BookedBy { get; init; }

    /// <summary>Who last changed it, if anyone has.</summary>
    public Person? UpdatedBy { get; init; }

    /// <summary>Whether the caller can change or cancel it.</summary>
    public required bool CanManage { get; init; }
}

public class Person
{
    public required Guid MemberId { get; init; }

    public required string DisplayName { get; init; }

    public Guid? UnitId { get; init; }
}

public static class BookingViews
{
    /// <summary>Bookings as the caller sees them: with the facility and people named, and times in the organisation's zone.</summary>
    public static async Task<List<BookingResponse>> ToResponsesAsync(ISqlSugarClient sql, ITenantContext context, IReadOnlyList<DataBooking> rows, CancellationToken ct)
    {
        var tenant = context.Tenant;
        var tenantId = tenant.Id;
        var zone = TenantTimeZone.Of(tenant);
        var facilities = (await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToListAsync(ct)).ToDictionary(f => f.Id);
        // Including those who have left: what they booked is still theirs
        var members = (await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId).ToListAsync(ct)).ToDictionary(m => m.Id);

        Person? PersonOf(Guid? id) =>
            id is { } memberId && members.TryGetValue(memberId, out var m) ? new Person { MemberId = m.Id, DisplayName = m.DisplayName, UnitId = m.UnitId } : null;

        return rows
            .Select(row =>
            {
                members.TryGetValue(row.BookedByMemberId, out var bookedBy);
                return new BookingResponse
                {
                    Id = row.Id,
                    FacilityId = row.FacilityId,
                    FacilityName = facilities.GetValueOrDefault(row.FacilityId)?.Name,
                    StartDateTime = TimeZoneInfo.ConvertTime(row.StartUtc, zone),
                    EndDateTime = TimeZoneInfo.ConvertTime(row.EndUtc, zone),
                    Conduct = row.Conduct,
                    Description = row.Description,
                    PocName = row.PocName,
                    PocPhone = row.PocPhone,
                    BookedBy = PersonOf(row.BookedByMemberId) ?? new Person { MemberId = row.BookedByMemberId, DisplayName = "Unknown" },
                    UpdatedBy = PersonOf(row.UpdatedByMemberId),
                    CanManage = BookingRules.CanManage(context.Member, bookedBy),
                };
            })
            .ToList();
    }
}
