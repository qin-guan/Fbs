using System.Net;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

public class BookingCancelTests
{
    [ClassDataSource<FbsApiFactory>]
    public required FbsApiFactory Factory { get; init; }

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
        Factory.Google.AddBooking(booking);
        return booking;
    }

    private async Task<HttpResponseMessage> CancelAsync(string phone, Guid id)
    {
        using var client = Factory.CreateClientFor(phone);
        return await client.DeleteAsync($"/Booking/{id}");
    }

    [Test]
    public async Task The_booker_can_cancel_their_booking()
    {
        var booking = AddBooking();

        var response = await CancelAsync(Users.Booker, booking.Id);

        await Assert.That(response).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).IsEmpty();
        await Assert.That(Factory.Google.Events(FakeGoogle.CarbonCopyCalendar)).IsEmpty();
    }

    [Test]
    public async Task Someone_in_the_same_unit_can_cancel_the_booking_and_the_booker_is_told_who()
    {
        var booking = AddBooking();

        var response = await CancelAsync(Users.SameUnit, booking.Id);

        await Assert.That(response).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).IsEmpty();
        await Assert.That(Factory.Google.Events(FakeGoogle.CarbonCopyCalendar)).IsEmpty();

        var messages = await Factory.Telegram.WaitForMessagesAsync(3);
        var toBooker = await Assert.That(messages).HasSingleItem(m => m.ChatId == 1001);
        await Assert.That(toBooker.Text).Contains("CANCELLED");
        await Assert.That(toBooker.Text).Contains("Name: LTA Same Unit");
        await Assert.That(toBooker.Text).Contains(booking.Id.ToString());
    }

    [Test]
    public async Task Other_units_cannot_cancel_the_booking()
    {
        var booking = AddBooking();

        var response = await CancelAsync(Users.OtherUnit, booking.Id);

        await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).HasSingleItem();
        await Assert.That(Factory.Google.Events(FakeGoogle.CarbonCopyCalendar)).HasSingleItem();
        await Task.Delay(200);
        await Assert.That(Factory.Telegram.Messages).IsEmpty();
    }

    [Test]
    public async Task Cancelling_a_missing_booking_is_not_found()
    {
        var response = await CancelAsync(Users.Booker, Guid.NewGuid());

        await Assert.That(response).HasStatus(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Requires_a_signed_in_user()
    {
        var booking = AddBooking();
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.DeleteAsync($"/Booking/{booking.Id}");

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).HasSingleItem();
    }
}
