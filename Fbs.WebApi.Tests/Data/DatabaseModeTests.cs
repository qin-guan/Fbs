using System.Net;
using System.Net.Http.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Tests.Helpers;
using Booking = Fbs.WebApi.Entities.Booking;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// With <c>Storage:Provider=Database</c> everything is in the database, and nothing is asked of Google.
/// </summary>
[ClassDataSource<DatabaseFbsApiFactory>]
public class DatabaseModeTests(DatabaseFbsApiFactory factory)
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private static DateTimeOffset Midnight(int daysFromNow)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(daysFromNow);
    }

    [Test]
    public async Task Booking_uses_no_google_service()
    {
        using var client = factory.CreateClientFor(Users.Booker);

        (await client.GetAsync("/Facility")).EnsureSuccessStatusCode();
        var created = await client.PostAsJsonAsync(
            "/Booking",
            new
            {
                conduct = "Range",
                facilityName = "Field",
                startDateTime = Midnight(3).AddHours(8),
                endDateTime = Midnight(3).AddHours(10),
                pocName = "CPT Booker",
                pocPhone = "6591234567",
            }
        );
        await Assert.That(created).HasStatus(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;
        (await client.GetAsync("/Booking")).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/Booking/{id}", new { conduct = "Range practice" })).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/Booking/{id}")).EnsureSuccessStatusCode();
        using var admin = factory.CreateClientFor(Users.Admin);
        (await admin.GetAsync("/Cache/Purge")).EnsureSuccessStatusCode();

        await Assert.That(factory.Google.Requests).IsEmpty();
    }

    [Test]
    public async Task The_point_of_contact_is_kept_with_the_booking_and_the_booker_stays_theirs()
    {
        using var booker = factory.CreateClientFor(Users.Booker);
        var created = await booker.PostAsJsonAsync(
            "/Booking",
            new
            {
                conduct = "Range",
                facilityName = "Field",
                startDateTime = Midnight(3).AddHours(8),
                endDateTime = Midnight(3).AddHours(10),
                pocName = "SGT Someone Else",
                pocPhone = "6598765432",
            }
        );
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;

        // Someone in the same unit changes it
        using var colleague = factory.CreateClientFor(Users.SameUnit);
        (await colleague.PostAsJsonAsync($"/Booking/{id}", new { conduct = "Range practice", pocName = "SGT Someone Else", pocPhone = "6598765432" }))
            .EnsureSuccessStatusCode();

        var row = factory.Db.Queryable<DataBooking>().Single(b => b.Id == id);
        var members = factory.Db.Queryable<TenantMember>().Where(m => m.TenantId == factory.TenantId).ToList();
        await Assert.That(row.PocName).IsEqualTo("SGT Someone Else");
        await Assert.That(row.PocPhone).IsEqualTo("6598765432");
        await Assert.That(members.Single(m => m.Id == row.BookedByMemberId).DisplayName).IsEqualTo("CPT Booker");
        await Assert.That(members.Single(m => m.Id == row.UpdatedByMemberId).DisplayName).IsEqualTo("LTA Same Unit");
        var listed = (await booker.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/Booking"))!.Single();
        await Assert.That(listed.GetProperty("user").GetProperty("name").GetString()).IsEqualTo("CPT Booker");
    }

    [Test]
    public async Task A_cancelled_booking_stays_in_the_database()
    {
        using var client = factory.CreateClientFor(Users.Booker);
        var created = await client.PostAsJsonAsync(
            "/Booking",
            new
            {
                conduct = "Range",
                facilityName = "Field",
                startDateTime = Midnight(3).AddHours(8),
                endDateTime = Midnight(3).AddHours(10),
            }
        );
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;

        (await client.DeleteAsync($"/Booking/{id}")).EnsureSuccessStatusCode();

        var row = factory.Db.Queryable<DataBooking>().Single(b => b.Id == id);
        await Assert.That(row.CancelledAt).IsNotNull();
        await Assert.That(await client.GetAsync($"/Booking/{id}")).HasStatus(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_telegram_outage_is_ridden_out_without_telling_anyone_twice()
    {
        factory.Telegram.FailingChatIds.Add(1003);
        using var client = factory.CreateClientFor(Users.Booker);

        var created = await client.PostAsJsonAsync(
            "/Booking",
            new
            {
                conduct = "Range",
                facilityName = "Field",
                startDateTime = Midnight(3).AddHours(8),
                endDateTime = Midnight(3).AddHours(10),
            }
        );
        await Assert.That(created).HasStatus(HttpStatusCode.Created);

        // The two whose chats work are told at once, and the message is put back to try the third again later
        await factory.Telegram.WaitForMessagesAsync(2);
        var tenantId = factory.TenantId;
        OutboxMessage? message = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && message?.LastError is null)
        {
            await Task.Delay(100);
            message = factory.Db.Queryable<OutboxMessage>().Single(m => m.TenantId == tenantId);
        }

        await Assert.That(message!.Status).IsEqualTo(OutboxStatus.Pending);
        await Assert.That(message.LastError).IsNotNull();
        await Assert.That(factory.Telegram.Messages.Select(m => m.ChatId)).IsEquivalentTo([1001L, 1002L]);

        factory.Telegram.FailingChatIds.Clear();
        factory.Db.Updateable<OutboxMessage>().SetColumns(m => new OutboxMessage { NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1) }).Where(m => m.TenantId == tenantId).ExecuteCommand();
        await factory.Telegram.WaitForMessagesAsync(3);
        await Task.Delay(500);

        // Each told once, including those told before it failed
        await Assert.That(factory.Telegram.Messages.Select(m => m.ChatId)).IsEquivalentTo([1001L, 1002L, 1003L]);
        await Assert.That(factory.Db.Queryable<OutboxMessage>().Single(m => m.TenantId == tenantId).Status).IsEqualTo(OutboxStatus.Done);
    }
}
