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

public class BookingTests
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    [ClassDataSource<FbsApiFactory>]
    public required FbsApiFactory Factory { get; init; }

    private HttpClient _client = null!;

    [Before(Test)]
    public void CreateClient()
    {
        _client = Factory.CreateClientFor(Users.Booker);
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

    private async Task<List<JsonElement>> ListAsync(HttpClient? client = null) =>
        (await (client ?? _client).GetFromJsonAsync<List<JsonElement>>("/Booking"))!;

    /// <summary>Does what the background sync does every few seconds.</summary>
    private Task SyncAsync() => Factory.Services.GetRequiredService<BookingCache>().SyncAsync();

    /// <summary>Adds bookings to the calendar as if they were made before the test.</summary>
    private async Task AddExistingAsync(params Booking[] bookings)
    {
        foreach (var booking in bookings)
        {
            await Factory.AddBookingAsync(booking);
        }
    }

    private int FullCalendarLists() =>
        Factory.Google.Requests.Count(r => r.IsFullEventList(FakeGoogle.MainCalendar));

    private int CalendarLists() =>
        Factory.Google.Requests.Count(r => r.IsEventList(FakeGoogle.MainCalendar));

    [Test]
    public async Task Listing_bookings_does_not_call_the_calendar_once_they_are_loaded()
    {
        await AddExistingAsync(Existing("Eiger", Midnight(5), Midnight(6)));
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
    public async Task Created_updated_and_deleted_bookings_are_listed_straight_away_without_reloading_the_calendar()
    {
        await ListAsync();

        var created = await CreateAsync("Field", Midnight(3).AddHours(8), Midnight(3).AddHours(10));
        await Assert.That(created).HasStatus(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;

        var listed = await Assert.That(await ListAsync()).HasSingleItem();
        await Assert.That(listed.GetProperty("id").GetGuid()).IsEqualTo(id);
        await Assert.That(listed.GetProperty("conduct").GetString()).IsEqualTo("Range");
        await Assert.That(listed.GetProperty("user").GetProperty("name").GetString()).IsEqualTo("CPT Booker");

        var updated = await _client.PostAsJsonAsync($"/Booking/{id}", new { conduct = "Range practice" });
        await Assert.That(updated).HasStatus(HttpStatusCode.Created);
        var relisted = await Assert.That(await ListAsync()).HasSingleItem();
        await Assert.That(relisted.GetProperty("conduct").GetString()).IsEqualTo("Range practice");
        await Assert
            .That((await _client.GetFromJsonAsync<JsonElement>($"/Booking/{id}")).GetProperty("conduct").GetString())
            .IsEqualTo("Range practice");

        var deleted = await _client.DeleteAsync($"/Booking/{id}");
        await Assert.That(deleted).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(await ListAsync()).IsEmpty();
        await Assert.That(await _client.GetAsync($"/Booking/{id}")).HasStatus(HttpStatusCode.NotFound);

        await Assert.That(FullCalendarLists()).IsEqualTo(1);
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).IsEmpty();
        await Assert.That(Factory.Google.Events(FakeGoogle.CarbonCopyCalendar)).IsEmpty();
    }

    [Test]
    public async Task Changes_made_directly_in_the_calendar_are_picked_up_by_the_sync()
    {
        var changed = Existing("Eiger", Midnight(5), Midnight(6));
        var removed = Existing("Field", Midnight(5), Midnight(6));
        await AddExistingAsync(changed, removed);
        await Assert.That(await ListAsync()).Count().IsEqualTo(2);

        var added = Existing("Gym", Midnight(5), Midnight(6));
        Factory.Google.AddBooking(added);
        changed.Conduct = "Changed";
        Factory.Google.AddBooking(changed);
        Factory.Google.RemoveBooking(removed.Id);
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
        Factory.Google.AddBooking(added);
        Factory.Google.ExpireSyncTokens();
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
        Factory.Google.AddBooking(existing);

        var response = await CreateAsync("Field", Midnight(4).AddHours(8), Midnight(4).AddHours(12));

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(existing.Id.ToString());
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).HasSingleItem();
    }

    [Test]
    public async Task Purging_the_cache_reloads_bookings()
    {
        await ListAsync();
        Factory.Google.AddBooking(Existing("Gym", Midnight(5), Midnight(6)));

        using var admin = Factory.CreateClientFor(Users.Admin);
        (await admin.GetAsync("/Cache/Purge")).EnsureSuccessStatusCode();

        await Assert.That(await ListAsync()).HasSingleItem();
        await Assert.That(FullCalendarLists()).IsEqualTo(2);
    }

    [Test]
    public async Task Bookings_by_users_no_longer_on_the_users_sheet_are_still_listed()
    {
        var orphaned = Existing("Eiger", Midnight(5), Midnight(6));
        orphaned.UserPhone = "6500000000";
        await AddExistingAsync(orphaned, Existing("Field", Midnight(5), Midnight(6)));

        var bookings = await ListAsync();

        await Assert.That(bookings).Count().IsEqualTo(2);
        await Assert
            .That(bookings.Single(b => b.GetProperty("id").GetGuid() == orphaned.Id).GetProperty("user").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Bookings_are_compressed()
    {
        await AddExistingAsync(Existing("Eiger", Midnight(5), Midnight(6)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Booking");
        request.Headers.AcceptEncoding.ParseAdd("br");

        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        await Assert.That(response.Content.Headers.ContentEncoding).IsEquivalentTo(["br"]);
    }

    [Test]
    public async Task Responds_without_waiting_for_notifications_to_be_sent()
    {
        Factory.Telegram.Pause();
        var timeout = TimeSpan.FromSeconds(5);

        var created = await CreateAsync("Field", Midnight(3).AddHours(8), Midnight(3).AddHours(10)).WaitAsync(timeout);
        await Assert.That(created).HasStatus(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;

        var updated = await _client
            .PostAsJsonAsync($"/Booking/{id}", new { conduct = "Range practice" })
            .WaitAsync(timeout);
        await Assert.That(updated).HasStatus(HttpStatusCode.Created);

        var deleted = await _client.DeleteAsync($"/Booking/{id}").WaitAsync(timeout);
        await Assert.That(deleted).HasStatus(HttpStatusCode.NoContent);

        await Assert.That(Factory.Telegram.Messages).IsEmpty();
        Factory.Telegram.Resume();

        // The booker, their unit and the "All" group hear about each change, in order
        var messages = await Factory.Telegram.WaitForMessagesAsync(9);
        await Assert.That(messages).Count().IsEqualTo(9);
        await Assert.That(messages.Take(3)).All().Satisfy(m => m.Text, text => text.Contains("CREATED"));
        await Assert.That(messages.Skip(3).Take(3)).All().Satisfy(m => m.Text, text => text.Contains("UPDATED"));
        await Assert.That(messages.Skip(6)).All().Satisfy(m => m.Text, text => text.Contains("CANCELLED"));
    }

    [Test]
    public async Task Admins_see_edit_and_delete_bookings()
    {
        using var admin = Factory.CreateClientFor(Users.Admin);
        var existing = Existing("Eiger", Midnight(5), Midnight(6));
        await AddExistingAsync(existing);

        var listed = await Assert
            .That((await admin.GetFromJsonAsync<List<JsonElement>>("/Admin/Bookings"))!)
            .HasSingleItem();
        await Assert.That(listed.GetProperty("user").GetProperty("name").GetString()).IsEqualTo("LTA Same Unit");

        var edited = await admin.PutAsJsonAsync($"/Admin/Bookings/{existing.Id}", new { conduct = "Edited" });
        await Assert.That(edited).HasStatus(HttpStatusCode.OK);
        var relisted = await Assert.That(await ListAsync()).HasSingleItem();
        await Assert.That(relisted.GetProperty("conduct").GetString()).IsEqualTo("Edited");

        var deleted = await admin.DeleteAsync($"/Admin/Bookings/{existing.Id}");
        await Assert.That(deleted).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(await ListAsync()).IsEmpty();
        await Assert.That(Factory.Google.Events(FakeGoogle.MainCalendar)).IsEmpty();

        // The booker, their unit and the "All" group hear about both changes
        await Assert.That(await Factory.Telegram.WaitForMessagesAsync(6)).Count().IsEqualTo(6);
    }
}
