using FastEndpoints;
using Fbs.WebApi.Dtos;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Facility.TimeSlots.Get;

public class Endpoint(
    FacilityRepository facilityRepository,
    UserRepository userRepository,
    BookingRepository bookingRepository
) : Endpoint<Request, IEnumerable<TimeSlot>>
{
    public override void Configure()
    {
        Get("/Facility/{Name}/TimeSlots");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        if (req.StartTime >= req.EndTime || req.StartTime.Minute % 30 != 0 || req.EndTime.Minute % 30 != 0)
        {
            throw new Exception(
                "Either the start time is greater than end time, or the time values provided are not in the correct intervals.");
        }

        var facility = await facilityRepository.GetAsync(f => f.Name == req.Name, ct);

        var users = await userRepository.GetListAsync(ct);
        var bookings = await bookingRepository.GetListAsync(ct);
        var overlapping = bookings
            .Where(b => b.FacilityName == facility.Name)
            .Where(b => b.StartDateTime <= req.EndTime && b.EndDateTime >= req.StartTime)
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

        var current = req.StartTime;
        var slots = new List<TimeSlot>();
        do
        {
            var newCurrent = current;
            var newEnd = current.AddMinutes(30);
            current = current.AddMinutes(30);

            var overlaps = overlapping.SingleOrDefault(b => b.StartDateTime < newEnd && b.EndDateTime > newCurrent);
            slots.Add(new TimeSlot
            {
                StartDateTime = newCurrent,
                EndDateTime = newEnd,
                Booking = overlaps
            });
        } while (!ct.IsCancellationRequested && current < req.EndTime);

        await Send.OkAsync(slots, ct);
    }
}