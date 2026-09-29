using Fbs.WebApi.Bookings;
using Fbs.WebApi.Entities;

namespace Fbs.WebApi.Tests;

public class BookingOverlapsTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(8));

    private static Booking At(string facility, int fromHour, int toHour, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            FacilityName = facility,
            StartDateTime = Nine.AddHours(fromHour - 9),
            EndDateTime = Nine.AddHours(toHour - 9),
        };

    [Test]
    [Arguments(9, 11, 10, 12, true)] // Overlapping at one end
    [Arguments(10, 12, 9, 11, true)] // ...and the other
    [Arguments(9, 13, 10, 11, true)] // One inside the other
    [Arguments(10, 11, 9, 13, true)]
    [Arguments(9, 11, 9, 11, true)] // The same time
    [Arguments(9, 10, 10, 11, false)] // One ends as the next starts
    [Arguments(10, 11, 9, 10, false)]
    [Arguments(9, 10, 11, 12, false)] // Apart
    public async Task Bookings_of_a_facility_clash_when_they_share_any_time(
        int aFrom, int aTo, int bFrom, int bTo, bool clash)
    {
        await Assert.That(BookingOverlaps.Overlap(At("Field", aFrom, aTo), At("Field", bFrom, bTo))).IsEqualTo(clash);
    }

    [Test]
    public async Task Different_facilities_never_clash()
    {
        await Assert.That(BookingOverlaps.Overlap(At("Field", 9, 11), At("Gym", 9, 11))).IsFalse();
    }

    [Test]
    public async Task A_booking_that_clashes_with_an_existing_one_names_it()
    {
        var existing = At("Field", 9, 11);

        var conflicts = BookingOverlaps.Find([At("Field", 10, 12)], [existing, At("Gym", 10, 12)]);

        var conflict = await Assert.That(conflicts).HasSingleItem();
        await Assert.That(conflict).IsEqualTo(new BookingConflict(0, existing.Id, null));
    }

    [Test]
    public async Task Bookings_in_the_same_list_clash_with_the_earlier_one()
    {
        var conflicts = BookingOverlaps.Find([At("Field", 9, 11), At("Field", 10, 12)], []);

        var conflict = await Assert.That(conflicts).HasSingleItem();
        await Assert.That(conflict).IsEqualTo(new BookingConflict(1, null, 0));
    }

    [Test]
    public async Task A_clash_with_an_existing_booking_is_reported_in_preference_to_one_in_the_list()
    {
        var existing = At("Field", 10, 11);

        var conflicts = BookingOverlaps.Find([At("Field", 9, 11), At("Field", 10, 12)], [existing]);

        // Both clash with the existing booking, and the second also with the first
        await Assert.That(conflicts.Select(c => (c.Index, c.WithBookingId, c.WithEarlierIndex)))
            .IsEquivalentTo([(0, (Guid?)existing.Id, (int?)null), (1, (Guid?)existing.Id, (int?)null)]);
    }

    [Test]
    public async Task Every_clash_is_reported_not_just_the_first()
    {
        var conflicts = BookingOverlaps.Find(
            [At("Field", 9, 10), At("Field", 9, 10), At("Gym", 9, 10), At("Gym", 9, 10)],
            []
        );

        await Assert.That(conflicts.Select(c => c.Index)).IsEquivalentTo([1, 3]);
    }

    [Test]
    public async Task Bookings_that_do_not_clash_have_no_conflicts()
    {
        var conflicts = BookingOverlaps.Find([At("Field", 9, 10), At("Field", 10, 11)], [At("Field", 11, 12)]);

        await Assert.That(conflicts).IsEmpty();
    }
}
