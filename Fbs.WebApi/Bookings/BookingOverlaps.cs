using Fbs.WebApi.Entities;

namespace Fbs.WebApi.Bookings;

/// <summary>
/// What counts as a clash, the same whichever store the bookings are in.
/// </summary>
public static class BookingOverlaps
{
    /// <summary>Whether the bookings are of the same facility and share any time. Touching ends don't.</summary>
    public static bool Overlap(Booking a, Booking b)
    {
        return a.FacilityName == b.FacilityName
            && a.StartDateTime < b.EndDateTime
            && a.EndDateTime > b.StartDateTime;
    }

    /// <summary>
    /// Every one of <paramref name="bookings"/> that clashes with a booking that is there already, or
    /// failing that with an earlier one in the list.
    /// </summary>
    public static List<BookingConflict> Find(IReadOnlyList<Booking> bookings, IEnumerable<Booking> existing)
    {
        var existingBookings = existing as IReadOnlyCollection<Booking> ?? existing.ToList();
        var conflicts = new List<BookingConflict>();
        for (var i = 0; i < bookings.Count; i++)
        {
            var booking = bookings[i];

            var overlapping = existingBookings.FirstOrDefault(b => Overlap(b, booking));
            if (overlapping is not null)
            {
                conflicts.Add(new BookingConflict(i, overlapping.Id, null));
                continue;
            }

            var earlier = FindIndex(bookings, i, booking);
            if (earlier >= 0)
            {
                conflicts.Add(new BookingConflict(i, null, earlier));
            }
        }

        return conflicts;
    }

    private static int FindIndex(IReadOnlyList<Booking> bookings, int before, Booking booking)
    {
        for (var i = 0; i < before; i++)
        {
            if (Overlap(bookings[i], booking))
            {
                return i;
            }
        }

        return -1;
    }
}
