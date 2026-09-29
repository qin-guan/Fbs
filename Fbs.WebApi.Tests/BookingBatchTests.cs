using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using TUnit.Assertions.Enums;

namespace Fbs.WebApi.Tests;

public abstract class BookingBatchTests(FbsApiFactory factory)
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    protected FbsApiFactory Factory { get; } = factory;

    private HttpClient _client = null!;

    [Before(Test)]
    public void CreateClient()
    {
        _client = Factory.CreateClientFor(Users.Booker);
    }

    /// <summary>Makes three bookings together, and what they came back as.</summary>
    protected async Task<List<Booking>> CreateThreeAsync()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Field", Midnight(11).AddHours(8), Midnight(11).AddHours(12)),
            Slot("Field", Midnight(12).AddHours(8), Midnight(12).AddHours(12))
        );
        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<Booking>>())!;
    }

    /// <summary>Midnight (Singapore time) a number of days from now.</summary>
    private static DateTimeOffset Midnight(int daysFromNow)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(daysFromNow);
    }

    private static object Slot(string facility, DateTimeOffset start, DateTimeOffset end) =>
        new { facilityName = facility, startDateTime = start, endDateTime = end };

    private Task<HttpResponseMessage> PostBatchAsync(params object[] slots) =>
        _client.PostAsJsonAsync(
            "/Booking/Batch",
            new
            {
                conduct = "Section training",
                description = "Bring water",
                pocName = "CPT Booker",
                pocPhone = "6591234567",
                slots,
            }
        );

    private static async Task<List<(string Name, string Reason, string? Code)>> ErrorsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json
            .RootElement.GetProperty("errors")
            .EnumerateArray()
            .Select(e => (
                e.GetProperty("name").GetString()!,
                e.GetProperty("reason").GetString()!,
                e.TryGetProperty("code", out var code) ? code.GetString() : null
            ))
            .ToList();
    }

    [Test]
    public async Task Creates_every_slot()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Field", Midnight(11).AddHours(8), Midnight(11).AddHours(12)),
            Slot("Field", Midnight(12).AddHours(8), Midnight(12).AddHours(12))
        );

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var created = (await response.Content.ReadFromJsonAsync<List<Booking>>())!;
        await Assert.That(created).Count().IsEqualTo(3);
        await Assert.That(created).All(b => b.Id != Guid.Empty);
        await Assert.That(created).All(b => b.Conduct == "Section training");
        await Assert
            .That(created.Select(b => b.FacilityName))
            .IsEquivalentTo(new string?[] { "Eiger", "Field", "Field" }, CollectionOrdering.Matching);
        await Assert.That(created[0].StartDateTime).IsEqualTo(Midnight(10));
        await Assert.That(created[0].EndDateTime).IsEqualTo(Midnight(11));

        await Factory.AssertStoredBookingsAsync(3);
    }

    [Test]
    public async Task Created_bookings_are_listed()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Field", Midnight(10), Midnight(11))
        );
        await Assert.That(response).HasStatus(HttpStatusCode.OK);

        var bookings = await _client.GetFromJsonAsync<List<JsonElement>>("/Booking");
        await Assert.That(bookings!).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Consecutive_whole_days_do_not_clash()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Eiger", Midnight(11), Midnight(12)),
            Slot("Eiger", Midnight(12), Midnight(13))
        );

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        await Factory.AssertStoredBookingsAsync(3);
    }

    [Test]
    public async Task A_multi_day_slot_is_one_booking()
    {
        var response = await PostBatchAsync(Slot("Eiger", Midnight(10), Midnight(13)));

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var created = await Assert.That((await response.Content.ReadFromJsonAsync<List<Booking>>())!).HasSingleItem();
        await Assert.That(created.EndDateTime).IsEqualTo(Midnight(13));
    }

    [Test]
    public async Task A_clash_with_an_existing_booking_fails_the_whole_batch()
    {
        var existing = new Booking
        {
            Id = Guid.NewGuid(),
            FacilityName = "Field",
            Conduct = "Existing",
            StartDateTime = Midnight(11).AddHours(9),
            EndDateTime = Midnight(11).AddHours(10),
            UserPhone = Users.SameUnit,
        };
        await Factory.AddBookingAsync(existing);

        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(11), Midnight(12)),
            Slot("Field", Midnight(11).AddHours(8), Midnight(11).AddHours(12))
        );

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        var error = await Assert.That(await ErrorsAsync(response)).HasSingleItem();
        await Assert.That(error.Name).IsEqualTo("slots[1]");
        await Assert.That(error.Code).IsEqualTo("EX10");
        await Assert.That(error.Reason).Contains(existing.Id.ToString());

        await Factory.AssertStoredBookingsAsync(1);
        await Task.Delay(200);
        await Assert.That(Factory.Telegram.Messages).IsEmpty();
    }

    [Test]
    public async Task Overlapping_slots_in_the_same_batch_fail_the_whole_batch()
    {
        var response = await PostBatchAsync(
            Slot("Field", Midnight(11).AddHours(8), Midnight(11).AddHours(12)),
            Slot("Eiger", Midnight(11).AddHours(8), Midnight(11).AddHours(12)),
            Slot("Field", Midnight(11).AddHours(11), Midnight(11).AddHours(13))
        );

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        var error = await Assert.That(await ErrorsAsync(response)).HasSingleItem();
        await Assert.That(error.Name).IsEqualTo("slots[2]");
        await Assert.That(error.Code).IsEqualTo("EX11");
        await Assert.That(error.Reason).IsEqualTo("Overlaps with slot 1 in this batch");
        await Factory.AssertStoredBookingsAsync(0);
    }

    [Test]
    public async Task Every_clash_is_reported()
    {
        await Factory.AddBookingAsync(new Booking
        {
            Id = Guid.NewGuid(),
            FacilityName = "Eiger",
            StartDateTime = Midnight(10).AddHours(7),
            EndDateTime = Midnight(10).AddHours(9),
            UserPhone = Users.SameUnit,
        });

        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Field", Midnight(10), Midnight(11)),
            Slot("Field", Midnight(10).AddHours(12), Midnight(10).AddHours(14))
        );

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        var errors = await ErrorsAsync(response);
        await Assert.That(errors.Select(e => e.Name)).IsEquivalentTo(["slots[0]", "slots[2]"], CollectionOrdering.Matching);
        await Assert.That(errors.Select(e => e.Code)).IsEquivalentTo(new string?[] { "EX10", "EX11" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Facilities_the_user_may_not_book_fail_the_whole_batch()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Gym", Midnight(10), Midnight(11)),
            Slot("Moon Base", Midnight(10), Midnight(11))
        );

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        var errors = await ErrorsAsync(response);
        await Assert.That(errors.Select(e => e.Name)).IsEquivalentTo(["slots[1]", "slots[2]"], CollectionOrdering.Matching);
        await Assert.That(errors.Select(e => e.Code)).IsEquivalentTo(new string?[] { "EX13", "EX12" }, CollectionOrdering.Matching);
        await Factory.AssertStoredBookingsAsync(0);
    }

    [Test]
    public async Task Invalid_slots_are_rejected()
    {
        var past = await PostBatchAsync(Slot("Eiger", Midnight(-1), Midnight(0)));
        await Assert.That(past).HasStatus(HttpStatusCode.BadRequest);
        var pastError = await Assert.That(await ErrorsAsync(past)).HasSingleItem();
        await Assert.That(pastError.Reason).IsEqualTo("Start Date Time must be in the future");
        await Assert.That(pastError.Name).StartsWith("slots[0]");

        var unaligned = await PostBatchAsync(Slot("Eiger", Midnight(10).AddMinutes(15), Midnight(11)));
        await Assert.That(unaligned).HasStatus(HttpStatusCode.BadRequest);
        await Assert
            .That(await ErrorsAsync(unaligned))
            .Contains(e => e.Reason == "Duration must be in 30 minute intervals");

        var backwards = await PostBatchAsync(Slot("Eiger", Midnight(11), Midnight(10)));
        await Assert.That(backwards).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await ErrorsAsync(backwards)).Contains(e => e.Reason == "End time must be after start time");

        var empty = await PostBatchAsync();
        await Assert.That(empty).HasStatus(HttpStatusCode.BadRequest);

        var tooMany = await PostBatchAsync(
            Enumerable.Range(0, 51).Select(i => Slot("Eiger", Midnight(10 + i), Midnight(11 + i))).ToArray()
        );
        await Assert.That(tooMany).HasStatus(HttpStatusCode.BadRequest);
        await Assert
            .That(await ErrorsAsync(tooMany))
            .Contains(e => e.Reason == "A batch can have at most 50 bookings");

        await Factory.AssertStoredBookingsAsync(0);
    }

    [Test]
    public async Task Conduct_is_required()
    {
        var response = await _client.PostAsJsonAsync(
            "/Booking/Batch",
            new { conduct = "", slots = new[] { Slot("Eiger", Midnight(10), Midnight(11)) } }
        );

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        await Factory.AssertStoredBookingsAsync(0);
    }

    [Test]
    public async Task Concurrent_overlapping_batches_cannot_both_succeed()
    {
        using var otherClient = Factory.CreateClientFor(Users.SameUnit);
        var body = new
        {
            conduct = "Race",
            slots = new[] { Slot("Eiger", Midnight(10), Midnight(11)), Slot("Field", Midnight(10), Midnight(11)) },
        };

        var responses = await Task.WhenAll(
            _client.PostAsJsonAsync("/Booking/Batch", body),
            otherClient.PostAsJsonAsync("/Booking/Batch", body)
        );

        await Assert.That(responses).HasSingleItem(r => r.StatusCode == HttpStatusCode.OK);
        await Assert.That(responses).HasSingleItem(r => r.StatusCode == HttpStatusCode.BadRequest);
        await Factory.AssertStoredBookingsAsync(2);
    }

    [Test]
    public async Task Requires_a_signed_in_user()
    {
        using var anonymous = Factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/Booking/Batch", new { conduct = "x", slots = Array.Empty<object>() });

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Single_bookings_still_work_and_clash_with_batch_bookings()
    {
        var batch = await PostBatchAsync(Slot("Eiger", Midnight(10), Midnight(11)));
        await Assert.That(batch).HasStatus(HttpStatusCode.OK);

        var single = await _client.PostAsJsonAsync(
            "/Booking",
            new
            {
                conduct = "Single",
                facilityName = "Field",
                startDateTime = Midnight(10).AddHours(8),
                endDateTime = Midnight(10).AddHours(9),
            }
        );
        await Assert.That(single).HasStatus(HttpStatusCode.Created);

        var clash = await _client.PostAsJsonAsync(
            "/Booking",
            new
            {
                conduct = "Clash",
                facilityName = "Eiger",
                startDateTime = Midnight(10).AddHours(8),
                endDateTime = Midnight(10).AddHours(9),
            }
        );
        await Assert.That(clash).HasStatus(HttpStatusCode.BadRequest);
        await Factory.AssertStoredBookingsAsync(2);
    }
}

/// <summary>BookingBatchTests with users, facilities, the roster and login codes in Google Sheets.</summary>
[ClassDataSource<FbsApiFactory>]
[InheritsTests]
public class GoogleBookingBatchTests(FbsApiFactory factory) : BookingBatchTests(factory)
{
    [Test]
    public async Task Sends_a_telegram_message_for_each_booking()
    {
        var created = await CreateThreeAsync();

        // One message per booking to the booker, their unit and the "All" group; none to other units
        var messages = await Factory.Telegram.WaitForMessagesAsync(9);
        await Assert.That(messages).Count().IsEqualTo(9);
        await Assert.That(messages.Count(m => m.ChatId == 1001)).IsEqualTo(3);
        await Assert.That(messages.Count(m => m.ChatId == 1002)).IsEqualTo(3);
        await Assert.That(messages.Count(m => m.ChatId == 1003)).IsEqualTo(3);
        await Assert.That(messages).DoesNotContain(m => m.ChatId == 1004);
        foreach (var booking in created)
        {
            await Assert.That(messages.Count(m => m.Text.Contains(booking.Id.ToString()))).IsEqualTo(3);
        }
    }
}

/// <summary>BookingBatchTests with users, facilities, the roster and login codes in the database.</summary>
[ClassDataSource<DatabaseFbsApiFactory>]
[InheritsTests]
public class DatabaseBookingBatchTests(DatabaseFbsApiFactory factory) : BookingBatchTests(factory)
{
    [Test]
    public async Task Sends_one_message_about_all_the_bookings_to_each_person()
    {
        var created = await CreateThreeAsync();

        // One message to the booker, their unit and the "All" group, not one for each booking; none to other units
        var messages = await Factory.Telegram.WaitForMessagesAsync(3);
        await Task.Delay(300);
        messages = Factory.Telegram.Messages.ToList();
        await Assert.That(messages).Count().IsEqualTo(3);
        await Assert.That(messages.Select(m => m.ChatId)).IsEquivalentTo([1001L, 1002L, 1003L]);
        var texts = messages.Select(m => m.Text).ToList();
        await Assert.That(texts.All(t => t.Contains("CREATED") && t.Contains("3 bookings"))).IsTrue();
        // Each booking is listed, with a short reference
        await Assert.That(texts.All(t => t.Contains("Eiger") && t.Contains("Field"))).IsTrue();
        foreach (var booking in created)
        {
            await Assert.That(texts.All(t => t.Contains(booking.Id.ToString("N")[..8]))).IsTrue();
        }
    }
}
