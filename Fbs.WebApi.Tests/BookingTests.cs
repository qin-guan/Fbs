using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

public abstract class BookingTests(FbsApiFactory factory)
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    protected FbsApiFactory Factory { get; } = factory;

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

    /// <summary>Adds bookings as if they were made before the test.</summary>
    private async Task AddExistingAsync(params Booking[] bookings)
    {
        foreach (var booking in bookings)
        {
            await Factory.AddBookingAsync(booking);
        }
    }

    [Test]
    public async Task Created_updated_and_deleted_bookings_are_listed_straight_away()
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

        await Factory.AssertStoredBookingsAsync(0);
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
        await Factory.AssertStoredBookingsAsync(0);

        // The booker, their unit and the "All" group hear about both changes
        await Assert.That(await Factory.Telegram.WaitForMessagesAsync(6)).Count().IsEqualTo(6);
    }
}

/// <summary>BookingTests with users, facilities, the roster and login codes in Google Sheets.</summary>
[ClassDataSource<FbsApiFactory>]
[InheritsTests]
public class GoogleBookingTests(FbsApiFactory factory) : BookingTests(factory);

/// <summary>BookingTests with users, facilities, the roster and login codes in the database.</summary>
[ClassDataSource<DatabaseFbsApiFactory>]
[InheritsTests]
public class DatabaseBookingTests(DatabaseFbsApiFactory factory) : BookingTests(factory);
