using System.Runtime.CompilerServices;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Types;

[ExtendObjectType<Facility>]
public class FacilityTypeExtension(BookingRepository bookingRepository, UserRepository userRepository)
{
    public async IAsyncEnumerable<TimeSlot> AvailableTimeSlots(
        [Parent] Facility facility,
        DateTimeOffset start,
        DateTimeOffset end,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        if (start >= end || start.Minute % 30 != 0 || end.Minute % 30 != 0)
        {
            throw new Exception();
        }

        var users = await userRepository.GetListAsync(ct);
        var bookings = await bookingRepository.GetListAsync(ct);
        var overlapping = bookings
            .Where(b => b.FacilityName == facility.Name)
            .Where(b => b.StartDateTime <= end && b.EndDateTime >= start)
            .Select(booking => new BookingWithUser
            {
                Id = booking.Id,
                FacilityName = booking.FacilityName,
                Conduct = booking.Conduct,
                Description = booking.Description,
                PocName = booking.PocName,
                PocPhone = booking.PocPhone,
                StartDateTime = booking.StartDateTime,
                EndDateTime = booking.EndDateTime,
                User = users.Single(u => u.Phone == booking.UserPhone),
            })
            .ToList();

        var current = start;
        do
        {
            var newCurrent = current;
            var newEnd = current.AddMinutes(30);
            current = current.AddMinutes(30);

            var overlaps = overlapping.SingleOrDefault(b => b.StartDateTime < newEnd && b.EndDateTime > newCurrent);

            yield return new TimeSlot(newCurrent, newEnd, overlaps);
        } while (!ct.IsCancellationRequested && current < end);
    }
}