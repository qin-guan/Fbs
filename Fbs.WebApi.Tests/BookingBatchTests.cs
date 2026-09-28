using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Fakes;

namespace Fbs.WebApi.Tests;

public class BookingBatchTests : IDisposable
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private readonly FbsApiFactory _factory = new();
    private readonly HttpClient _client;

    public BookingBatchTests()
    {
        _client = _factory.CreateClientFor(Users.Booker);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
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

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        Assert.True(
            response.StatusCode == expected,
            $"Expected {expected} but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }

    private int BatchEventCount(string calendarId) => _factory.Google.Events(calendarId).Count;

    [Fact]
    public async Task Creates_every_slot_and_sends_a_telegram_message_for_each_booking()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Field", Midnight(11).AddHours(8), Midnight(11).AddHours(12)),
            Slot("Field", Midnight(12).AddHours(8), Midnight(12).AddHours(12))
        );

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var created = await response.Content.ReadFromJsonAsync<List<Booking>>();
        Assert.Equal(3, created!.Count);
        Assert.All(created, b => Assert.NotEqual(Guid.Empty, b.Id));
        Assert.All(created, b => Assert.Equal("Section training", b.Conduct));
        Assert.Equal(["Eiger", "Field", "Field"], created.Select(b => b.FacilityName));
        Assert.Equal(Midnight(10), created[0].StartDateTime);
        Assert.Equal(Midnight(11), created[0].EndDateTime);

        Assert.Equal(3, BatchEventCount(FakeGoogle.MainCalendar));
        Assert.Equal(3, BatchEventCount(FakeGoogle.CarbonCopyCalendar));

        // One message per booking to the booker, their unit and the "All" group; none to other units
        var messages = await _factory.Telegram.WaitForMessagesAsync(9);
        Assert.Equal(9, messages.Count);
        Assert.Equal(3, messages.Count(m => m.ChatId == 1001));
        Assert.Equal(3, messages.Count(m => m.ChatId == 1002));
        Assert.Equal(3, messages.Count(m => m.ChatId == 1003));
        Assert.DoesNotContain(messages, m => m.ChatId == 1004);
        foreach (var booking in created)
        {
            Assert.Equal(3, messages.Count(m => m.Text.Contains(booking.Id.ToString())));
        }
    }

    [Fact]
    public async Task Created_bookings_are_listed()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Field", Midnight(10), Midnight(11))
        );
        await AssertStatusAsync(HttpStatusCode.OK, response);

        var bookings = await _client.GetFromJsonAsync<List<JsonElement>>("/Booking");
        Assert.Equal(2, bookings!.Count);
    }

    [Fact]
    public async Task Consecutive_whole_days_do_not_clash()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Eiger", Midnight(11), Midnight(12)),
            Slot("Eiger", Midnight(12), Midnight(13))
        );

        await AssertStatusAsync(HttpStatusCode.OK, response);
        Assert.Equal(3, BatchEventCount(FakeGoogle.MainCalendar));
    }

    [Fact]
    public async Task A_multi_day_slot_is_one_booking()
    {
        var response = await PostBatchAsync(Slot("Eiger", Midnight(10), Midnight(13)));

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var created = await response.Content.ReadFromJsonAsync<List<Booking>>();
        Assert.Single(created!);
        Assert.Equal(Midnight(13), created![0].EndDateTime);
    }

    [Fact]
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
        _factory.Google.AddBooking(existing);

        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(11), Midnight(12)),
            Slot("Field", Midnight(11).AddHours(8), Midnight(11).AddHours(12))
        );

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        var error = Assert.Single(await ErrorsAsync(response));
        Assert.Equal("slots[1]", error.Name);
        Assert.Equal("EX10", error.Code);
        Assert.Contains(existing.Id.ToString(), error.Reason);

        Assert.Equal(1, BatchEventCount(FakeGoogle.MainCalendar));
        Assert.Equal(1, BatchEventCount(FakeGoogle.CarbonCopyCalendar));
        await Task.Delay(200);
        Assert.Empty(_factory.Telegram.Messages);
    }

    [Fact]
    public async Task Overlapping_slots_in_the_same_batch_fail_the_whole_batch()
    {
        var response = await PostBatchAsync(
            Slot("Field", Midnight(11).AddHours(8), Midnight(11).AddHours(12)),
            Slot("Eiger", Midnight(11).AddHours(8), Midnight(11).AddHours(12)),
            Slot("Field", Midnight(11).AddHours(11), Midnight(11).AddHours(13))
        );

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        var error = Assert.Single(await ErrorsAsync(response));
        Assert.Equal("slots[2]", error.Name);
        Assert.Equal("EX11", error.Code);
        Assert.Equal("Overlaps with slot 1 in this batch", error.Reason);
        Assert.Equal(0, BatchEventCount(FakeGoogle.MainCalendar));
    }

    [Fact]
    public async Task Every_clash_is_reported()
    {
        _factory.Google.AddBooking(new Booking
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

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        var errors = await ErrorsAsync(response);
        Assert.Equal(["slots[0]", "slots[2]"], errors.Select(e => e.Name));
        Assert.Equal(["EX10", "EX11"], errors.Select(e => e.Code));
    }

    [Fact]
    public async Task Facilities_the_user_may_not_book_fail_the_whole_batch()
    {
        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Gym", Midnight(10), Midnight(11)),
            Slot("Moon Base", Midnight(10), Midnight(11))
        );

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        var errors = await ErrorsAsync(response);
        Assert.Equal(["slots[1]", "slots[2]"], errors.Select(e => e.Name));
        Assert.Equal(["EX13", "EX12"], errors.Select(e => e.Code));
        Assert.Equal(0, BatchEventCount(FakeGoogle.MainCalendar));
    }

    [Fact]
    public async Task Invalid_slots_are_rejected()
    {
        var past = await PostBatchAsync(Slot("Eiger", Midnight(-1), Midnight(0)));
        await AssertStatusAsync(HttpStatusCode.BadRequest, past);
        var pastError = Assert.Single(await ErrorsAsync(past));
        Assert.Equal("Start Date Time must be in the future", pastError.Reason);
        Assert.StartsWith("slots[0]", pastError.Name);

        var unaligned = await PostBatchAsync(Slot("Eiger", Midnight(10).AddMinutes(15), Midnight(11)));
        await AssertStatusAsync(HttpStatusCode.BadRequest, unaligned);
        Assert.Contains(await ErrorsAsync(unaligned), e => e.Reason == "Duration must be in 30 minute intervals");

        var backwards = await PostBatchAsync(Slot("Eiger", Midnight(11), Midnight(10)));
        await AssertStatusAsync(HttpStatusCode.BadRequest, backwards);
        Assert.Contains(await ErrorsAsync(backwards), e => e.Reason == "End time must be after start time");

        var empty = await PostBatchAsync();
        await AssertStatusAsync(HttpStatusCode.BadRequest, empty);

        var tooMany = await PostBatchAsync(
            Enumerable.Range(0, 51).Select(i => Slot("Eiger", Midnight(10 + i), Midnight(11 + i))).ToArray()
        );
        await AssertStatusAsync(HttpStatusCode.BadRequest, tooMany);
        Assert.Contains(await ErrorsAsync(tooMany), e => e.Reason == "A batch can have at most 50 bookings");

        Assert.Equal(0, BatchEventCount(FakeGoogle.MainCalendar));
    }

    [Fact]
    public async Task Conduct_is_required()
    {
        var response = await _client.PostAsJsonAsync(
            "/Booking/Batch",
            new { conduct = "", slots = new[] { Slot("Eiger", Midnight(10), Midnight(11)) } }
        );

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        Assert.Equal(0, BatchEventCount(FakeGoogle.MainCalendar));
    }

    [Fact]
    public async Task A_calendar_failure_part_way_through_rolls_back_the_batch()
    {
        // Fail one of the 8 inserts (main and carbon copy calendars for each booking), after others have succeeded
        _factory.Google.FailInsertNumber = 5;

        var response = await PostBatchAsync(
            Slot("Eiger", Midnight(10), Midnight(11)),
            Slot("Eiger", Midnight(11), Midnight(12)),
            Slot("Eiger", Midnight(12), Midnight(13)),
            Slot("Eiger", Midnight(13), Midnight(14))
        );

        await AssertStatusAsync(HttpStatusCode.InternalServerError, response);
        Assert.Equal(0, BatchEventCount(FakeGoogle.MainCalendar));
        Assert.Equal(0, BatchEventCount(FakeGoogle.CarbonCopyCalendar));
        await Task.Delay(200);
        Assert.Empty(_factory.Telegram.Messages);
    }

    [Fact]
    public async Task Concurrent_overlapping_batches_cannot_both_succeed()
    {
        using var otherClient = _factory.CreateClientFor(Users.SameUnit);
        var body = new
        {
            conduct = "Race",
            slots = new[] { Slot("Eiger", Midnight(10), Midnight(11)), Slot("Field", Midnight(10), Midnight(11)) },
        };

        var responses = await Task.WhenAll(
            _client.PostAsJsonAsync("/Booking/Batch", body),
            otherClient.PostAsJsonAsync("/Booking/Batch", body)
        );

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(2, BatchEventCount(FakeGoogle.MainCalendar));
    }

    [Fact]
    public async Task Requires_a_signed_in_user()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/Booking/Batch", new { conduct = "x", slots = Array.Empty<object>() });

        await AssertStatusAsync(HttpStatusCode.Unauthorized, response);
    }

    [Fact]
    public async Task Single_bookings_still_work_and_clash_with_batch_bookings()
    {
        var batch = await PostBatchAsync(Slot("Eiger", Midnight(10), Midnight(11)));
        await AssertStatusAsync(HttpStatusCode.OK, batch);

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
        await AssertStatusAsync(HttpStatusCode.Created, single);

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
        await AssertStatusAsync(HttpStatusCode.BadRequest, clash);
        Assert.Equal(2, BatchEventCount(FakeGoogle.MainCalendar));
    }
}
