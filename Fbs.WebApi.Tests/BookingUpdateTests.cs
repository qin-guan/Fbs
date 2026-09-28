using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Fakes;

namespace Fbs.WebApi.Tests;

public class BookingUpdateTests : IDisposable
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private readonly FbsApiFactory _factory = new();
    private readonly HttpClient _client;

    public BookingUpdateTests()
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

    private Booking AddBooking(string facility, DateTimeOffset start, DateTimeOffset end, string userPhone = Users.Booker)
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            FacilityName = facility,
            Conduct = "Section training",
            StartDateTime = start,
            EndDateTime = end,
            UserPhone = userPhone,
        };
        _factory.Google.AddBooking(booking);
        return booking;
    }

    private static Task<HttpResponseMessage> UpdateAsync(
        HttpClient client,
        Guid id,
        DateTimeOffset? start,
        DateTimeOffset? end,
        string conduct = "Section training"
    ) =>
        client.PostAsJsonAsync(
            $"/Booking/{id}",
            new
            {
                conduct,
                pocName = "CPT Booker",
                pocPhone = "6591234567",
                startDateTime = start,
                endDateTime = end,
            }
        );

    private async Task<(DateTimeOffset Start, DateTimeOffset End)> TimesAsync(Guid id)
    {
        var booking = await _client.GetFromJsonAsync<JsonElement>($"/Booking/{id}");
        return (
            booking.GetProperty("startDateTime").GetDateTimeOffset(),
            booking.GetProperty("endDateTime").GetDateTimeOffset()
        );
    }

    private static async Task<List<string>> ReasonsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json
            .RootElement.GetProperty("errors")
            .EnumerateArray()
            .Select(e => e.GetProperty("reason").GetString()!)
            .ToList();
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        Assert.True(
            response.StatusCode == expected,
            $"Expected {expected} but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }

    [Fact]
    public async Task Moves_a_booking_to_a_free_slot_and_tells_everyone_the_old_time()
    {
        var booking = AddBooking("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));

        var response = await UpdateAsync(_client, booking.Id, Midnight(11).AddHours(14), Midnight(11).AddHours(16));

        await AssertStatusAsync(HttpStatusCode.Created, response);
        Assert.Equal((Midnight(11).AddHours(14), Midnight(11).AddHours(16)), await TimesAsync(booking.Id));

        foreach (var calendarId in new[] { FakeGoogle.MainCalendar, FakeGoogle.CarbonCopyCalendar })
        {
            var @event = Assert.Single(_factory.Google.Events(calendarId));
            Assert.Equal(
                Midnight(11).AddHours(14),
                DateTimeOffset.Parse(@event["start"]!["dateTime"]!.GetValue<string>())
            );
        }

        var messages = await _factory.Telegram.WaitForMessagesAsync(3);
        Assert.Equal(3, messages.Count);
        Assert.All(messages, m => Assert.Contains("Previously", m.Text));
    }

    [Fact]
    public async Task A_booking_can_move_into_its_own_slot()
    {
        var booking = AddBooking("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));

        var response = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(9), Midnight(10).AddHours(11));

        await AssertStatusAsync(HttpStatusCode.Created, response);
        Assert.Equal((Midnight(10).AddHours(9), Midnight(10).AddHours(11)), await TimesAsync(booking.Id));
    }

    [Fact]
    public async Task Moving_onto_another_booking_is_rejected()
    {
        var booking = AddBooking("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        var other = AddBooking("Field", Midnight(10).AddHours(12), Midnight(10).AddHours(14), Users.OtherUnit);
        AddBooking("Eiger", Midnight(10).AddHours(9), Midnight(10).AddHours(13));

        var response = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(9), Midnight(10).AddHours(13));

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        var reason = Assert.Single(await ReasonsAsync(response));
        Assert.Contains(other.Id.ToString(), reason);
        Assert.Equal((Midnight(10).AddHours(8), Midnight(10).AddHours(10)), await TimesAsync(booking.Id));
        await Task.Delay(200);
        Assert.Empty(_factory.Telegram.Messages);
    }

    [Fact]
    public async Task Invalid_time_slots_are_rejected()
    {
        var booking = AddBooking("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));

        var past = await UpdateAsync(_client, booking.Id, Midnight(-1).AddHours(8), Midnight(-1).AddHours(10));
        await AssertStatusAsync(HttpStatusCode.BadRequest, past);
        Assert.Contains("Start Date Time must be in the future", await ReasonsAsync(past));

        var unaligned = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(8).AddMinutes(15), Midnight(10).AddHours(10));
        await AssertStatusAsync(HttpStatusCode.BadRequest, unaligned);
        Assert.Contains("Duration must be in 30 minute intervals", await ReasonsAsync(unaligned));

        var backwards = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(10), Midnight(10).AddHours(8));
        await AssertStatusAsync(HttpStatusCode.BadRequest, backwards);
        Assert.Contains("End time must be after start time", await ReasonsAsync(backwards));

        var onlyStart = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(9), null);
        await AssertStatusAsync(HttpStatusCode.BadRequest, onlyStart);

        Assert.Equal((Midnight(10).AddHours(8), Midnight(10).AddHours(10)), await TimesAsync(booking.Id));
    }

    [Fact]
    public async Task A_booking_that_is_over_cannot_be_moved_but_its_details_can_still_change()
    {
        var booking = AddBooking("Field", Midnight(-2).AddHours(8), Midnight(-2).AddHours(10));

        var move = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        await AssertStatusAsync(HttpStatusCode.BadRequest, move);
        Assert.Contains("This booking is over, so its time can no longer be changed", await ReasonsAsync(move));

        var details = await UpdateAsync(
            _client,
            booking.Id,
            booking.StartDateTime,
            booking.EndDateTime,
            conduct: "Renamed"
        );
        await AssertStatusAsync(HttpStatusCode.Created, details);
        Assert.Equal((booking.StartDateTime!.Value, booking.EndDateTime!.Value), await TimesAsync(booking.Id));
    }

    [Fact]
    public async Task Leaving_out_the_times_keeps_the_time_slot()
    {
        var booking = AddBooking("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));

        var response = await UpdateAsync(_client, booking.Id, null, null, conduct: "Renamed");

        await AssertStatusAsync(HttpStatusCode.Created, response);
        Assert.Equal((Midnight(10).AddHours(8), Midnight(10).AddHours(10)), await TimesAsync(booking.Id));
        var messages = await _factory.Telegram.WaitForMessagesAsync(3);
        Assert.All(messages, m => Assert.DoesNotContain("Previously", m.Text));
    }

    [Fact]
    public async Task Someone_in_the_same_unit_can_move_a_booking()
    {
        var booking = AddBooking("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        using var sameUnit = _factory.CreateClientFor(Users.SameUnit);

        var response = await UpdateAsync(sameUnit, booking.Id, Midnight(10).AddHours(12), Midnight(10).AddHours(14));

        await AssertStatusAsync(HttpStatusCode.Created, response);
    }

    [Fact]
    public async Task Other_units_cannot_update_a_booking()
    {
        var booking = AddBooking("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        using var otherUnit = _factory.CreateClientFor(Users.OtherUnit);

        var response = await UpdateAsync(otherUnit, booking.Id, Midnight(10).AddHours(12), Midnight(10).AddHours(14));

        await AssertStatusAsync(HttpStatusCode.Forbidden, response);
        Assert.Equal((Midnight(10).AddHours(8), Midnight(10).AddHours(10)), await TimesAsync(booking.Id));
    }

    [Fact]
    public async Task Updating_a_missing_booking_is_not_found()
    {
        var response = await UpdateAsync(_client, Guid.NewGuid(), null, null);

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }
}
