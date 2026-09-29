using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Fbs.WebApi.Tests;

/// <summary>
/// What is particular to keeping bookings in Google Calendar: the copies in two calendars, and the sync
/// that picks up changes made in the calendar. Goes when bookings do.
/// </summary>
[ClassDataSource<FbsApiFactory>]
public class CalendarBookingTests(FbsApiFactory factory)
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private HttpClient _client = null!;

    [Before(Test)]
    public void CreateClient()
    {
        _client = factory.CreateClientFor(Users.Booker);
    }

    private static DateTimeOffset Midnight(int daysFromNow)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(daysFromNow);
    }

    private static Booking Existing(string facility, DateTimeOffset start, DateTimeOffset end) =>
        new()
        {
            Id = Guid.NewGuid(),
            FacilityName = facility,
            Conduct = "Existing",
            StartDateTime = start,
            EndDateTime = end,
            UserPhone = Users.SameUnit,
        };

    private Task<HttpResponseMessage> CreateAsync(string facility, DateTimeOffset start, DateTimeOffset end) =>
        _client.PostAsJsonAsync(
            "/Booking",
            new
            {
                conduct = "Range",
                facilityName = facility,
                startDateTime = start,
                endDateTime = end,
            }
        );

    private async Task<List<JsonElement>> ListAsync() => (await _client.GetFromJsonAsync<List<JsonElement>>("/Booking"))!;

    /// <summary>Does what the background sync does every few seconds.</summary>
    private Task SyncAsync() => factory.Services.GetRequiredService<BookingCache>().SyncAsync();

    private int FullCalendarLists() => factory.Google.Requests.Count(r => r.IsFullEventList(FakeGoogle.MainCalendar));

    private int CalendarLists() => factory.Google.Requests.Count(r => r.IsEventList(FakeGoogle.MainCalendar));

    [Test]
    public async Task Listing_bookings_does_not_call_the_calendar_once_they_are_loaded()
    {
        await factory.AddBookingAsync(Existing("Eiger", Midnight(5), Midnight(6)));
        await Assert.That(await ListAsync()).HasSingleItem();
        var lists = CalendarLists();

        for (var i = 0; i < 5; i++)
        {
            await Assert.That(await ListAsync()).HasSingleItem();
        }

        await Assert.That(CalendarLists()).IsEqualTo(lists);
        await Assert.That(FullCalendarLists()).IsEqualTo(1);
    }

    [Test]
    public async Task Created_updated_and_deleted_bookings_are_listed_without_reloading_the_calendar()
    {
        await ListAsync();

        var created = await CreateAsync("Field", Midnight(3).AddHours(8), Midnight(3).AddHours(10));
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;
        await Assert.That(await ListAsync()).HasSingleItem();
        await _client.PostAsJsonAsync($"/Booking/{id}", new { conduct = "Range practice" });
        await Assert.That(await ListAsync()).HasSingleItem();
        await _client.DeleteAsync($"/Booking/{id}");
        await Assert.That(await ListAsync()).IsEmpty();

        await Assert.That(FullCalendarLists()).IsEqualTo(1);
        await Assert.That(factory.Google.Events(FakeGoogle.MainCalendar)).IsEmpty();
        await Assert.That(factory.Google.Events(FakeGoogle.CarbonCopyCalendar)).IsEmpty();
    }

    [Test]
    public async Task Changes_made_directly_in_the_calendar_are_picked_up_by_the_sync()
    {
        var changed = Existing("Eiger", Midnight(5), Midnight(6));
        var removed = Existing("Field", Midnight(5), Midnight(6));
        await factory.AddBookingAsync(changed);
        await factory.AddBookingAsync(removed);
        await Assert.That(await ListAsync()).Count().IsEqualTo(2);

        var added = Existing("Gym", Midnight(5), Midnight(6));
        factory.Google.AddBooking(added);
        changed.Conduct = "Changed";
        factory.Google.AddBooking(changed);
        factory.Google.RemoveBooking(removed.Id);
        await SyncAsync();

        var bookings = await ListAsync();
        await Assert.That(bookings.Select(b => b.GetProperty("id").GetGuid())).IsEquivalentTo([added.Id, changed.Id]);
        await Assert
            .That(bookings.Single(b => b.GetProperty("id").GetGuid() == changed.Id).GetProperty("conduct").GetString())
            .IsEqualTo("Changed");
        await Assert.That(FullCalendarLists()).IsEqualTo(1);
    }

    [Test]
    public async Task An_expired_sync_token_reloads_every_booking()
    {
        await ListAsync();

        var added = Existing("Gym", Midnight(5), Midnight(6));
        factory.Google.AddBooking(added);
        factory.Google.ExpireSyncTokens();
        await SyncAsync();

        var listed = await Assert.That(await ListAsync()).HasSingleItem();
        await Assert.That(listed.GetProperty("id").GetGuid()).IsEqualTo(added.Id);
        await Assert.That(FullCalendarLists()).IsEqualTo(2);
    }

    [Test]
    public async Task Clash_checks_include_bookings_made_directly_in_the_calendar_since_the_last_sync()
    {
        await ListAsync();
        var existing = Existing("Field", Midnight(4).AddHours(9), Midnight(4).AddHours(10));
        factory.Google.AddBooking(existing);

        var response = await CreateAsync("Field", Midnight(4).AddHours(8), Midnight(4).AddHours(12));

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(existing.Id.ToString());
        await Assert.That(factory.Google.Events(FakeGoogle.MainCalendar)).HasSingleItem();
    }

    [Test]
    public async Task Purging_the_cache_reloads_bookings()
    {
        await ListAsync();
        factory.Google.AddBooking(Existing("Gym", Midnight(5), Midnight(6)));

        using var admin = factory.CreateClientFor(Users.Admin);
        (await admin.GetAsync("/Cache/Purge")).EnsureSuccessStatusCode();

        await Assert.That(await ListAsync()).HasSingleItem();
        await Assert.That(FullCalendarLists()).IsEqualTo(2);
    }

    [Test]
    public async Task Moving_a_booking_moves_its_event_in_both_calendars()
    {
        var booking = Existing("Field", Midnight(10).AddHours(8), Midnight(10).AddHours(10));
        booking.UserPhone = Users.Booker;
        await factory.AddBookingAsync(booking);

        var response = await _client.PostAsJsonAsync(
            $"/Booking/{booking.Id}",
            new { conduct = "Moved", startDateTime = Midnight(11).AddHours(14), endDateTime = Midnight(11).AddHours(16) }
        );

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        foreach (var calendarId in new[] { FakeGoogle.MainCalendar, FakeGoogle.CarbonCopyCalendar })
        {
            var @event = await Assert.That(factory.Google.Events(calendarId)).HasSingleItem();
            await Assert
                .That(DateTimeOffset.Parse(@event["start"]!["dateTime"]!.GetValue<string>()))
                .IsEqualTo(Midnight(11).AddHours(14));
        }
    }

    [Test]
    public async Task Cancelling_succeeds_when_the_carbon_copy_event_was_already_removed()
    {
        // The carbon copy calendar accepts manual changes, so its event may already be gone
        var booking = Existing("Eiger", Midnight(10), Midnight(11));
        booking.UserPhone = Users.Booker;
        await factory.AddBookingAsync(booking);
        factory.Google.RemoveEvent(FakeGoogle.CarbonCopyCalendar, booking.Id);

        var response = await _client.DeleteAsync($"/Booking/{booking.Id}");

        await Assert.That(response).HasStatus(HttpStatusCode.NoContent);
        await factory.AssertStoredBookingsAsync(0);
    }

    [Test]
    public async Task A_calendar_failure_part_way_through_a_batch_rolls_it_back()
    {
        // Fail one of the 8 inserts (main and carbon copy calendars for each booking), after others have succeeded
        factory.Google.FailInsertNumber = 5;

        var response = await _client.PostAsJsonAsync(
            "/Booking/Batch",
            new
            {
                conduct = "Section training",
                slots = Enumerable
                    .Range(10, 4)
                    .Select(day => new { facilityName = "Eiger", startDateTime = Midnight(day), endDateTime = Midnight(day + 1) })
                    .ToArray(),
            }
        );

        await Assert.That(response).HasStatus(HttpStatusCode.InternalServerError);
        await factory.AssertStoredBookingsAsync(0);
        await Task.Delay(200);
        await Assert.That(factory.Telegram.Messages).IsEmpty();
    }
}
