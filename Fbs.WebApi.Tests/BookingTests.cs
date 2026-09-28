using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Fbs.WebApi.Tests;

public class BookingTests : IDisposable
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private readonly FbsApiFactory _factory = new();
    private readonly HttpClient _client;

    public BookingTests()
    {
        _client = _factory.CreateClientFor(Users.Booker);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
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
    private Task SyncAsync() => _factory.Services.GetRequiredService<BookingCache>().SyncAsync();

    /// <summary>Adds bookings to the calendar as if they were made before the test.</summary>
    private async Task AddExistingAsync(params Booking[] bookings)
    {
        foreach (var booking in bookings)
        {
            await _factory.AddBookingAsync(booking);
        }
    }

    private int FullCalendarLists() =>
        _factory.Google.Requests.Count(r => r.IsFullEventList(FakeGoogle.MainCalendar));

    private int CalendarLists() =>
        _factory.Google.Requests.Count(r => r.IsEventList(FakeGoogle.MainCalendar));

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        Assert.True(
            response.StatusCode == expected,
            $"Expected {expected} but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }

    [Fact]
    public async Task Listing_bookings_does_not_call_the_calendar_once_they_are_loaded()
    {
        await AddExistingAsync(Existing("Eiger", Midnight(5), Midnight(6)));
        Assert.Single(await ListAsync());
        var lists = CalendarLists();

        for (var i = 0; i < 5; i++)
        {
            Assert.Single(await ListAsync());
        }

        Assert.Equal(lists, CalendarLists());
        Assert.Equal(1, FullCalendarLists());
    }

    [Fact]
    public async Task Created_updated_and_deleted_bookings_are_listed_straight_away_without_reloading_the_calendar()
    {
        await ListAsync();

        var created = await CreateAsync("Field", Midnight(3).AddHours(8), Midnight(3).AddHours(10));
        await AssertStatusAsync(HttpStatusCode.Created, created);
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;

        var listed = Assert.Single(await ListAsync());
        Assert.Equal(id, listed.GetProperty("id").GetGuid());
        Assert.Equal("Range", listed.GetProperty("conduct").GetString());
        Assert.Equal("CPT Booker", listed.GetProperty("user").GetProperty("name").GetString());

        var updated = await _client.PostAsJsonAsync($"/Booking/{id}", new { conduct = "Range practice" });
        await AssertStatusAsync(HttpStatusCode.Created, updated);
        Assert.Equal("Range practice", Assert.Single(await ListAsync()).GetProperty("conduct").GetString());
        Assert.Equal(
            "Range practice",
            (await _client.GetFromJsonAsync<JsonElement>($"/Booking/{id}")).GetProperty("conduct").GetString()
        );

        var deleted = await _client.DeleteAsync($"/Booking/{id}");
        await AssertStatusAsync(HttpStatusCode.NoContent, deleted);
        Assert.Empty(await ListAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/Booking/{id}")).StatusCode);

        Assert.Equal(1, FullCalendarLists());
        Assert.Empty(_factory.Google.Events(FakeGoogle.MainCalendar));
        Assert.Empty(_factory.Google.Events(FakeGoogle.CarbonCopyCalendar));
    }

    [Fact]
    public async Task Changes_made_directly_in_the_calendar_are_picked_up_by_the_sync()
    {
        var changed = Existing("Eiger", Midnight(5), Midnight(6));
        var removed = Existing("Field", Midnight(5), Midnight(6));
        await AddExistingAsync(changed, removed);
        Assert.Equal(2, (await ListAsync()).Count);

        var added = Existing("Gym", Midnight(5), Midnight(6));
        _factory.Google.AddBooking(added);
        changed.Conduct = "Changed";
        _factory.Google.AddBooking(changed);
        _factory.Google.RemoveBooking(removed.Id);
        await SyncAsync();

        var bookings = await ListAsync();
        Assert.Equal(
            new[] { added.Id, changed.Id }.Order(),
            bookings.Select(b => b.GetProperty("id").GetGuid()).Order()
        );
        Assert.Equal(
            "Changed",
            bookings.Single(b => b.GetProperty("id").GetGuid() == changed.Id).GetProperty("conduct").GetString()
        );
        Assert.Equal(1, FullCalendarLists());
    }

    [Fact]
    public async Task An_expired_sync_token_reloads_every_booking()
    {
        await ListAsync();

        var added = Existing("Gym", Midnight(5), Midnight(6));
        _factory.Google.AddBooking(added);
        _factory.Google.ExpireSyncTokens();
        await SyncAsync();

        Assert.Equal(added.Id, Assert.Single(await ListAsync()).GetProperty("id").GetGuid());
        Assert.Equal(2, FullCalendarLists());
    }

    [Fact]
    public async Task Clash_checks_include_bookings_made_directly_in_the_calendar_since_the_last_sync()
    {
        await ListAsync();
        var existing = Existing("Field", Midnight(4).AddHours(9), Midnight(4).AddHours(10));
        _factory.Google.AddBooking(existing);

        var response = await CreateAsync("Field", Midnight(4).AddHours(8), Midnight(4).AddHours(12));

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        Assert.Contains(existing.Id.ToString(), await response.Content.ReadAsStringAsync());
        Assert.Single(_factory.Google.Events(FakeGoogle.MainCalendar));
    }

    [Fact]
    public async Task Purging_the_cache_reloads_bookings()
    {
        await ListAsync();
        _factory.Google.AddBooking(Existing("Gym", Midnight(5), Midnight(6)));

        (await _client.GetAsync("/Cache/Purge")).EnsureSuccessStatusCode();

        Assert.Single(await ListAsync());
        Assert.Equal(2, FullCalendarLists());
    }

    [Fact]
    public async Task Bookings_by_users_no_longer_on_the_users_sheet_are_still_listed()
    {
        var orphaned = Existing("Eiger", Midnight(5), Midnight(6));
        orphaned.UserPhone = "6500000000";
        await AddExistingAsync(orphaned, Existing("Field", Midnight(5), Midnight(6)));

        var bookings = await ListAsync();

        Assert.Equal(2, bookings.Count);
        Assert.Equal(
            JsonValueKind.Null,
            bookings.Single(b => b.GetProperty("id").GetGuid() == orphaned.Id).GetProperty("user").ValueKind
        );
    }

    [Fact]
    public async Task Bookings_are_compressed()
    {
        await AddExistingAsync(Existing("Eiger", Midnight(5), Midnight(6)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Booking");
        request.Headers.AcceptEncoding.ParseAdd("br");

        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(["br"], response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task Responds_without_waiting_for_notifications_to_be_sent()
    {
        _factory.Telegram.Pause();
        var timeout = TimeSpan.FromSeconds(5);

        var created = await CreateAsync("Field", Midnight(3).AddHours(8), Midnight(3).AddHours(10)).WaitAsync(timeout);
        await AssertStatusAsync(HttpStatusCode.Created, created);
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;

        var updated = await _client
            .PostAsJsonAsync($"/Booking/{id}", new { conduct = "Range practice" })
            .WaitAsync(timeout);
        await AssertStatusAsync(HttpStatusCode.Created, updated);

        var deleted = await _client.DeleteAsync($"/Booking/{id}").WaitAsync(timeout);
        await AssertStatusAsync(HttpStatusCode.NoContent, deleted);

        Assert.Empty(_factory.Telegram.Messages);
        _factory.Telegram.Resume();

        // The booker, their unit and the "All" group hear about each change, in order
        var messages = await _factory.Telegram.WaitForMessagesAsync(9);
        Assert.Equal(9, messages.Count);
        Assert.All(messages.Take(3), m => Assert.Contains("CREATED", m.Text));
        Assert.All(messages.Skip(3).Take(3), m => Assert.Contains("UPDATED", m.Text));
        Assert.All(messages.Skip(6), m => Assert.Contains("CANCELLED", m.Text));
    }

    [Fact]
    public async Task Admins_see_edit_and_delete_bookings()
    {
        using var admin = _factory.CreateClientFor(Users.Admin);
        var existing = Existing("Eiger", Midnight(5), Midnight(6));
        await AddExistingAsync(existing);

        var listed = Assert.Single((await admin.GetFromJsonAsync<List<JsonElement>>("/Admin/Bookings"))!);
        Assert.Equal("LTA Same Unit", listed.GetProperty("user").GetProperty("name").GetString());

        var edited = await admin.PutAsJsonAsync($"/Admin/Bookings/{existing.Id}", new { conduct = "Edited" });
        await AssertStatusAsync(HttpStatusCode.OK, edited);
        Assert.Equal("Edited", Assert.Single(await ListAsync()).GetProperty("conduct").GetString());

        var deleted = await admin.DeleteAsync($"/Admin/Bookings/{existing.Id}");
        await AssertStatusAsync(HttpStatusCode.NoContent, deleted);
        Assert.Empty(await ListAsync());
        Assert.Empty(_factory.Google.Events(FakeGoogle.MainCalendar));

        // The booker, their unit and the "All" group hear about both changes
        Assert.Equal(6, (await _factory.Telegram.WaitForMessagesAsync(6)).Count);
    }
}
