using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

public abstract class BookingUpdateTests(FbsApiFactory factory)
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    protected FbsApiFactory Factory { get; } = factory;

    private HttpClient _client = null!;

    [Before(Test)]
    public void CreateClient()
    {
        _client = Factory.CreateClientFor(Users.Booker);
    }

    /// <summary>Midnight (Singapore time) a number of days from now.</summary>
    private static DateTimeOffset Midnight(int daysFromNow)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(daysFromNow);
    }

    private async Task<Booking> AddBookingAsync(string facility, DateTimeOffset start, DateTimeOffset end, string userPhone = Users.Booker)
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
        await Factory.AddBookingAsync(booking);
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

    [Test]
    public async Task Moves_a_booking_to_a_free_slot_and_tells_everyone_the_old_time()
    {
        var booking = await AddBookingAsync("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));

        var response = await UpdateAsync(_client, booking.Id, Midnight(11).AddHours(14), Midnight(11).AddHours(16));

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        await Assert.That(await TimesAsync(booking.Id)).IsEqualTo((Midnight(11).AddHours(14), Midnight(11).AddHours(16)));

        await Factory.AssertStoredBookingsAsync(1);

        var messages = await Factory.Telegram.WaitForMessagesAsync(3);
        await Assert.That(messages).Count().IsEqualTo(3);
        await Assert.That(messages).All().Satisfy(m => m.Text, text => text.Contains("Was:"));
    }

    [Test]
    public async Task A_booking_can_move_into_its_own_slot()
    {
        var booking = await AddBookingAsync("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));

        var response = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(9), Midnight(10).AddHours(11));

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        await Assert.That(await TimesAsync(booking.Id)).IsEqualTo((Midnight(10).AddHours(9), Midnight(10).AddHours(11)));
    }

    [Test]
    public async Task Moving_onto_another_booking_is_rejected()
    {
        var booking = await AddBookingAsync("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        var other = await AddBookingAsync("Field", Midnight(10).AddHours(12), Midnight(10).AddHours(14), Users.OtherUnit);
        await AddBookingAsync("Eiger", Midnight(10).AddHours(9), Midnight(10).AddHours(13));

        var response = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(9), Midnight(10).AddHours(13));

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        var reason = await Assert.That(await ReasonsAsync(response)).HasSingleItem();
        await Assert.That(reason).Contains(other.Id.ToString());
        await Assert.That(await TimesAsync(booking.Id)).IsEqualTo((Midnight(10).AddHours(8), Midnight(10).AddHours(10)));
        await Task.Delay(200);
        await Assert.That(Factory.Telegram.Messages).IsEmpty();
    }

    [Test]
    public async Task Invalid_time_slots_are_rejected()
    {
        var booking = await AddBookingAsync("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));

        var past = await UpdateAsync(_client, booking.Id, Midnight(-1).AddHours(8), Midnight(-1).AddHours(10));
        await Assert.That(past).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await ReasonsAsync(past)).Contains("Start Date Time must be in the future");

        var unaligned = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(8).AddMinutes(15), Midnight(10).AddHours(10));
        await Assert.That(unaligned).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await ReasonsAsync(unaligned)).Contains("Duration must be in 30 minute intervals");

        var backwards = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(10), Midnight(10).AddHours(8));
        await Assert.That(backwards).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await ReasonsAsync(backwards)).Contains("End time must be after start time");

        var onlyStart = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(9), null);
        await Assert.That(onlyStart).HasStatus(HttpStatusCode.BadRequest);

        await Assert.That(await TimesAsync(booking.Id)).IsEqualTo((Midnight(10).AddHours(8), Midnight(10).AddHours(10)));
    }

    [Test]
    public async Task A_booking_that_is_over_cannot_be_moved_but_its_details_can_still_change()
    {
        var booking = await AddBookingAsync("Field", Midnight(-2).AddHours(8), Midnight(-2).AddHours(10));

        var move = await UpdateAsync(_client, booking.Id, Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        await Assert.That(move).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await ReasonsAsync(move)).Contains("This booking is over, so its time can no longer be changed");

        var details = await UpdateAsync(
            _client,
            booking.Id,
            booking.StartDateTime,
            booking.EndDateTime,
            conduct: "Renamed"
        );
        await Assert.That(details).HasStatus(HttpStatusCode.Created);
        await Assert.That(await TimesAsync(booking.Id)).IsEqualTo((booking.StartDateTime!.Value, booking.EndDateTime!.Value));
    }

    [Test]
    public async Task Leaving_out_the_times_keeps_the_time_slot()
    {
        var booking = await AddBookingAsync("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));

        var response = await UpdateAsync(_client, booking.Id, null, null, conduct: "Renamed");

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        await Assert.That(await TimesAsync(booking.Id)).IsEqualTo((Midnight(10).AddHours(8), Midnight(10).AddHours(10)));
        var messages = await Factory.Telegram.WaitForMessagesAsync(3);
        await Assert.That(messages).All().Satisfy(m => m.Text, text => text.DoesNotContain("Was:"));
    }

    [Test]
    public async Task Someone_in_the_same_unit_can_move_a_booking()
    {
        var booking = await AddBookingAsync("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        using var sameUnit = Factory.CreateClientFor(Users.SameUnit);

        var response = await UpdateAsync(sameUnit, booking.Id, Midnight(10).AddHours(12), Midnight(10).AddHours(14));

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
    }

    [Test]
    public async Task Other_units_cannot_update_a_booking()
    {
        var booking = await AddBookingAsync("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        using var otherUnit = Factory.CreateClientFor(Users.OtherUnit);

        var response = await UpdateAsync(otherUnit, booking.Id, Midnight(10).AddHours(12), Midnight(10).AddHours(14));

        await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await TimesAsync(booking.Id)).IsEqualTo((Midnight(10).AddHours(8), Midnight(10).AddHours(10)));
    }

    [Test]
    public async Task Updating_a_missing_booking_is_not_found()
    {
        var response = await UpdateAsync(_client, Guid.NewGuid(), null, null);

        await Assert.That(response).HasStatus(HttpStatusCode.NotFound);
    }
}

/// <summary>BookingUpdateTests with users, facilities, the roster and login codes in Google Sheets.</summary>
[ClassDataSource<FbsApiFactory>]
[InheritsTests]
public class GoogleBookingUpdateTests(FbsApiFactory factory) : BookingUpdateTests(factory);

/// <summary>BookingUpdateTests with users, facilities, the roster and login codes in the database.</summary>
[ClassDataSource<DatabaseFbsApiFactory>]
[InheritsTests]
public class DatabaseBookingUpdateTests(DatabaseFbsApiFactory factory) : BookingUpdateTests(factory);
