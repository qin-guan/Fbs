using Fbs.WebApi.Data.Entities;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Bookings;

/// <summary>Who can do what with bookings of an organisation, and when they can start and end.</summary>
public static class BookingRules
{
    /// <summary>Admins can book anything. Others can book what is available to all, and what their unit has been given.</summary>
    public static bool CanBook(TenantMember member, DataFacility facility, IReadOnlySet<Guid> unitsWithAccess) =>
        member.Role == MemberRole.Admin
        || facility.AvailableToAll
        || (member.UnitId is { } unit && unitsWithAccess.Contains(unit));

    /// <summary>
    /// Whether the member can change or cancel a booking: their own, those of somebody in their unit, and any if they
    /// are an admin.
    /// </summary>
    public static bool CanManage(TenantMember member, TenantMember? bookedBy) =>
        member.Role == MemberRole.Admin
        || bookedBy?.Id == member.Id
        || (member.UnitId is { } unit && bookedBy?.UnitId == unit);

    /// <summary>
    /// Whether the time is on a slot, which is worked out in the organisation's time zone, not in whichever offset
    /// it was sent in.
    /// </summary>
    public static bool IsOnSlot(DateTimeOffset time, TimeZoneInfo zone, int slotMinutes) =>
        TimeZoneInfo.ConvertTime(time, zone).TimeOfDay.Ticks % TimeSpan.FromMinutes(slotMinutes).Ticks == 0;
}
