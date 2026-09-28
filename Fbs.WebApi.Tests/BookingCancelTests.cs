using System.Net;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Fakes;

namespace Fbs.WebApi.Tests;

public class BookingCancelTests : IDisposable
{
    private readonly FbsApiFactory _factory = new();

    public void Dispose()
    {
        _factory.Dispose();
    }

    private Booking AddBooking(string userPhone = Users.Booker)
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(10), TimeSpan.Zero);
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            FacilityName = "Field",
            Conduct = "Section training",
            StartDateTime = start,
            EndDateTime = start.AddHours(2),
            UserPhone = userPhone,
        };
        _factory.Google.AddBooking(booking);
        return booking;
    }

    private async Task<HttpResponseMessage> CancelAsync(string phone, Guid id)
    {
        using var client = _factory.CreateClientFor(phone);
        return await client.DeleteAsync($"/Booking/{id}");
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        Assert.True(
            response.StatusCode == expected,
            $"Expected {expected} but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }

    [Fact]
    public async Task The_booker_can_cancel_their_booking()
    {
        var booking = AddBooking();

        var response = await CancelAsync(Users.Booker, booking.Id);

        await AssertStatusAsync(HttpStatusCode.NoContent, response);
        Assert.Empty(_factory.Google.Events(FakeGoogle.MainCalendar));
        Assert.Empty(_factory.Google.Events(FakeGoogle.CarbonCopyCalendar));
    }

    [Fact]
    public async Task Someone_in_the_same_unit_can_cancel_the_booking_and_the_booker_is_told_who()
    {
        var booking = AddBooking();

        var response = await CancelAsync(Users.SameUnit, booking.Id);

        await AssertStatusAsync(HttpStatusCode.NoContent, response);
        Assert.Empty(_factory.Google.Events(FakeGoogle.MainCalendar));
        Assert.Empty(_factory.Google.Events(FakeGoogle.CarbonCopyCalendar));

        var messages = await _factory.Telegram.WaitForMessagesAsync(3);
        var toBooker = Assert.Single(messages, m => m.ChatId == 1001);
        Assert.Contains("CANCELLED", toBooker.Text);
        Assert.Contains("Name: LTA Same Unit", toBooker.Text);
        Assert.Contains(booking.Id.ToString(), toBooker.Text);
    }

    [Fact]
    public async Task Other_units_cannot_cancel_the_booking()
    {
        var booking = AddBooking();

        var response = await CancelAsync(Users.OtherUnit, booking.Id);

        await AssertStatusAsync(HttpStatusCode.Forbidden, response);
        Assert.Single(_factory.Google.Events(FakeGoogle.MainCalendar));
        Assert.Single(_factory.Google.Events(FakeGoogle.CarbonCopyCalendar));
        await Task.Delay(200);
        Assert.Empty(_factory.Telegram.Messages);
    }

    [Fact]
    public async Task Cancelling_a_missing_booking_is_not_found()
    {
        var response = await CancelAsync(Users.Booker, Guid.NewGuid());

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Requires_a_signed_in_user()
    {
        var booking = AddBooking();
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.DeleteAsync($"/Booking/{booking.Id}");

        await AssertStatusAsync(HttpStatusCode.Unauthorized, response);
        Assert.Single(_factory.Google.Events(FakeGoogle.MainCalendar));
    }
}
