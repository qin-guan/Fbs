using Fbs.WebApi.Notifications;

namespace Fbs.WebApi.Tests;

public class TelegramBookingMessagesTests
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);
    private static readonly Person Booker = new("Alpha", "CPT Booker", "6591234567");

    private static DateTimeOffset At(int day, int hour) => new(2026, 10, day, hour, 0, 0, Singapore);

    private static BookingSummary Summary(string facility = "Field", int day = 3, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), facility, At(day, 8), At(day, 10));

    [Test]
    public async Task A_single_booking_says_what_who_and_when_in_the_time_it_is_given()
    {
        var id = Guid.NewGuid();

        var text = TelegramBookingMessages.Build(
            BookingChange.Created,
            [Summary(id: id)],
            "Range practice",
            "Bring water",
            "SGT Poc",
            "6598765432",
            Booker
        );

        await Assert
            .That(text)
            .IsEqualTo(
                $"<b>CREATED</b> · <b>Field</b>\nRange practice\nSaturday, 03 October 2026 08:00 – Saturday, 03 October 2026 10:00\nPOC: SGT Poc, 6598765432\nBooked by: Alpha / CPT Booker, 6591234567\nBring water\nRef: {id}"
            );
    }

    [Test]
    public async Task What_people_typed_cannot_break_the_message()
    {
        var text = TelegramBookingMessages.Build(
            BookingChange.Created,
            [Summary(facility: "R&D <Hall>")],
            "<b>Bold</b> & more",
            "1 < 2 && 3 > 2",
            "<i>Poc</i>",
            "1",
            new Person("A&B", "<Name>", "1")
        );

        await Assert.That(text).DoesNotContain("<Hall>");
        await Assert.That(text).DoesNotContain("<Name>");
        await Assert.That(text).DoesNotContain("<i>Poc");
        await Assert.That(text).Contains("R&amp;D &lt;Hall&gt;");
        await Assert.That(text).Contains("&lt;b&gt;Bold&lt;/b&gt; &amp; more");
        await Assert.That(text).Contains("A&amp;B / &lt;Name&gt;");
    }

    [Test]
    public async Task An_update_says_where_it_was_and_who_changed_it()
    {
        var text = TelegramBookingMessages.Build(
            BookingChange.Updated,
            [Summary(day: 4)],
            "Range practice",
            null,
            "SGT Poc",
            "6598765432",
            new Person("Bravo", "LTA Colleague", "6591000002"),
            previousStart: At(3, 8),
            previousEnd: At(3, 10)
        );

        await Assert.That(text).StartsWith("<b>UPDATED</b> · <b>Field</b>");
        await Assert.That(text).Contains("\nWas: Saturday, 03 October 2026 08:00 – Saturday, 03 October 2026 10:00");
        await Assert.That(text).Contains("Updated by: Bravo / LTA Colleague, 6591000002");
    }

    [Test]
    public async Task An_update_that_did_not_move_it_has_no_was()
    {
        var text = TelegramBookingMessages.Build(BookingChange.Updated, [Summary()], "x", null, null, null, Booker);

        await Assert.That(text).DoesNotContain("Was:");
    }

    [Test]
    public async Task A_cancellation_says_who_cancelled_it()
    {
        var text = TelegramBookingMessages.Build(
            BookingChange.Cancelled,
            [Summary()],
            "Range practice",
            null,
            null,
            null,
            new Person("Bravo", "LTA Colleague", "6591000002")
        );

        await Assert.That(text).StartsWith("<b>CANCELLED</b> · <b>Field</b>");
        await Assert.That(text).Contains("Cancelled by: Bravo / LTA Colleague, 6591000002");
    }

    [Test]
    public async Task A_blank_description_leaves_no_empty_line()
    {
        var text = TelegramBookingMessages.Build(BookingChange.Created, [Summary()], "x", "  ", "Poc", "1", Booker);

        var lines = text.Split('\n');
        await Assert.That(lines.All(line => line.Length > 0)).IsTrue();
        await Assert.That(lines[^1]).StartsWith("Ref: ");
        await Assert.That(lines[^2]).StartsWith("Booked by: ");
    }

    [Test]
    public async Task Bookings_made_together_are_listed_in_one_message_with_short_references()
    {
        var batch = Guid.NewGuid();
        var bookings = new[] { Summary("Eiger", 3), Summary("Field", 4), Summary("Field", 5) };

        var text = TelegramBookingMessages.Build(BookingChange.Created, bookings, "Field camp", null, "Poc", "1", Booker, batchId: batch);

        await Assert.That(text).StartsWith("<b>CREATED</b> · <b>3 bookings</b>\nField camp");
        foreach (var booking in bookings)
        {
            await Assert.That(text).Contains($"<b>{booking.Facility}</b>");
            await Assert.That(text).Contains($"({booking.Id.ToString("N")[..8]})");
        }

        await Assert.That(text).Contains("Booked by: Alpha / CPT Booker, 6591234567");
        await Assert.That(text).EndsWith($"Ref: {batch}");
    }

    [Test]
    public async Task A_very_large_batch_is_cut_short_rather_than_refused_by_telegram()
    {
        var bookings = Enumerable.Range(0, 50).Select(i => Summary("A facility with quite a long name indeed", 1 + i % 28)).ToList();

        var text = TelegramBookingMessages.Build(
            BookingChange.Created,
            bookings,
            new string('c', 100),
            new string('d', 1500),
            "Poc",
            "1",
            Booker,
            batchId: Guid.NewGuid()
        );

        await Assert.That(text.Length).IsLessThanOrEqualTo(TelegramBookingMessages.MaxLength);
        await Assert.That(text).Contains("50 bookings");
        await Assert.That(text).Contains("…and ");
        await Assert.That(text).Contains(bookings[0].Id.ToString("N")[..8]);
        // Who, and what to refer to it by, are still there
        await Assert.That(text).Contains("Booked by: Alpha / CPT Booker");
        await Assert.That(text).Contains("Ref: ");
    }

    [Test]
    public async Task Times_are_spelled_out_the_same_whatever_language_the_server_is_in()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");

            var text = TelegramBookingMessages.Build(BookingChange.Created, [Summary()], "x", null, null, null, Booker);

            await Assert.That(text).Contains("Saturday, 03 October 2026 08:00");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }
}
