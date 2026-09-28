using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Fakes;

namespace Fbs.WebApi.Tests;

public class BookingDeleteTests : IAsyncLifetime
{
    private readonly FbsApiFactory _factory = new();
    private readonly HttpClient _client;
    private readonly Booking _booking = new()
    {
        Id = Guid.NewGuid(),
        FacilityName = "Eiger",
        Conduct = "Section training",
        StartDateTime = DateTimeOffset.UtcNow.AddDays(10),
        EndDateTime = DateTimeOffset.UtcNow.AddDays(10).AddHours(2),
        UserPhone = Users.Booker,
    };

    public BookingDeleteTests()
    {
        _client = _factory.CreateClientFor(Users.Booker);
    }

    public Task InitializeAsync() => _factory.AddBookingAsync(_booking);

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> DeleteAsync() => _client.DeleteAsync($"/Booking/{_booking.Id}");

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        Assert.True(
            response.StatusCode == expected,
            $"Expected {expected} but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }

    private async Task AssertBookingIsGoneAsync()
    {
        Assert.Empty(_factory.Google.Events(FakeGoogle.MainCalendar));
        Assert.Empty(_factory.Google.Events(FakeGoogle.CarbonCopyCalendar));

        var bookings = await _client.GetFromJsonAsync<List<JsonElement>>("/Booking");
        Assert.Empty(bookings!);
    }

    [Fact]
    public async Task Removes_the_booking_and_notifies_subscribers()
    {
        var response = await DeleteAsync();

        await AssertStatusAsync(HttpStatusCode.NoContent, response);
        await AssertBookingIsGoneAsync();

        // The booker, their unit and the "All" group; not other units
        var messages = await _factory.Telegram.WaitForMessagesAsync(3);
        Assert.Equal([1001, 1002, 1003], messages.Select(m => m.ChatId).Order());
        Assert.All(messages, m => Assert.Contains("CANCELLED", m.Text));
        Assert.All(messages, m => Assert.Contains(_booking.Id.ToString(), m.Text));
    }

    [Fact]
    public async Task Succeeds_when_the_carbon_copy_event_was_already_removed()
    {
        // The carbon copy calendar accepts manual changes, so its event may already be gone
        _factory.Google.RemoveEvent(FakeGoogle.CarbonCopyCalendar, _booking.Id);

        var response = await DeleteAsync();

        await AssertStatusAsync(HttpStatusCode.NoContent, response);
        await AssertBookingIsGoneAsync();
    }

    [Fact]
    public async Task Succeeds_when_a_notification_cannot_be_delivered()
    {
        _factory.Telegram.BlockedChatIds.Add(1003);

        var response = await DeleteAsync();

        await AssertStatusAsync(HttpStatusCode.NoContent, response);
        await AssertBookingIsGoneAsync();
    }

    [Fact]
    public async Task Returns_not_found_for_an_unknown_booking()
    {
        var response = await _client.DeleteAsync($"/Booking/{Guid.NewGuid()}");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
        Assert.Single(_factory.Google.Events(FakeGoogle.MainCalendar));
    }
}
