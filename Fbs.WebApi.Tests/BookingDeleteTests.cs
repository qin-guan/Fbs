using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

public class BookingDeleteTests
{
    [ClassDataSource<FbsApiFactory>]
    public required FbsApiFactory Factory { get; init; }

    private HttpClient _client = null!;
    private readonly Booking _booking = new()
    {
        Id = Guid.NewGuid(),
        FacilityName = "Eiger",
        Conduct = "Section training",
        StartDateTime = DateTimeOffset.UtcNow.AddDays(10),
        EndDateTime = DateTimeOffset.UtcNow.AddDays(10).AddHours(2),
        UserPhone = Users.Booker,
    };

    [Before(Test)]
    public async Task AddBookingAsync()
    {
        _client = Factory.CreateClientFor(Users.Booker);
        await Factory.AddBookingAsync(_booking);
    }

    private Task<HttpResponseMessage> DeleteAsync() => _client.DeleteAsync($"/Booking/{_booking.Id}");

    private async Task AssertBookingIsGoneAsync()
    {
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).IsEmpty();
        await Assert.That(Factory.Google.Events(FakeGoogle.CarbonCopyCalendar)).IsEmpty();

        var bookings = await _client.GetFromJsonAsync<List<JsonElement>>("/Booking");
        await Assert.That(bookings!).IsEmpty();
    }

    [Test]
    public async Task Removes_the_booking_and_notifies_subscribers()
    {
        var response = await DeleteAsync();

        await Assert.That(response).HasStatus(HttpStatusCode.NoContent);
        await AssertBookingIsGoneAsync();

        // The booker, their unit and the "All" group; not other units
        var messages = await Factory.Telegram.WaitForMessagesAsync(3);
        await Assert.That(messages.Select(m => m.ChatId)).IsEquivalentTo([1001L, 1002L, 1003L]);
        await Assert.That(messages).All().Satisfy(m => m.Text, text => text.Contains("CANCELLED"));
        await Assert.That(messages).All().Satisfy(m => m.Text, text => text.Contains(_booking.Id.ToString()));
    }

    [Test]
    public async Task Succeeds_when_the_carbon_copy_event_was_already_removed()
    {
        // The carbon copy calendar accepts manual changes, so its event may already be gone
        Factory.Google.RemoveEvent(FakeGoogle.CarbonCopyCalendar, _booking.Id);

        var response = await DeleteAsync();

        await Assert.That(response).HasStatus(HttpStatusCode.NoContent);
        await AssertBookingIsGoneAsync();
    }

    [Test]
    public async Task Succeeds_when_a_notification_cannot_be_delivered()
    {
        Factory.Telegram.BlockedChatIds.Add(1003);

        var response = await DeleteAsync();

        await Assert.That(response).HasStatus(HttpStatusCode.NoContent);
        await AssertBookingIsGoneAsync();
    }

    [Test]
    [Arguments("/Booking/{id}")]
    [Arguments("/Admin/Bookings/{id}")]
    public async Task Is_documented_as_having_no_response_body(string path)
    {
        // The generated web app client decodes whatever the spec says, so it must match the 204 sent
        var spec = JsonDocument.Parse(await _client.GetStringAsync("/openapi/v1.json"));
        var responses = spec.RootElement.GetProperty("paths").GetProperty(path).GetProperty("delete").GetProperty("responses");
        var statusCodes = responses.EnumerateObject().Select(r => r.Name).ToList();

        await Assert.That(statusCodes).Contains("204");
        await Assert.That(statusCodes).DoesNotContain("200");
        await Assert.That(responses.GetProperty("204").EnumerateObject().Select(p => p.Name)).DoesNotContain("content");
    }

    [Test]
    public async Task Returns_not_found_for_an_unknown_booking()
    {
        var response = await _client.DeleteAsync($"/Booking/{Guid.NewGuid()}");

        await Assert.That(response).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).HasSingleItem();
    }
}
