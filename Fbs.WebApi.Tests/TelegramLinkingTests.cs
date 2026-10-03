using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Tests;

/// <summary>Connecting an account to a Telegram chat from the app, and being told about bookings there.</summary>
public class TelegramLinkingTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static JsonObject Update(string text, long chat = 5551, string chatType = "private") =>
        new()
        {
            ["update_id"] = 1,
            ["message"] = new JsonObject
            {
                ["message_id"] = 1,
                ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["chat"] = new JsonObject { ["id"] = chat, ["type"] = chatType },
                ["from"] = new JsonObject { ["id"] = chat, ["is_bot"] = false, ["first_name"] = "Sender" },
                ["text"] = text,
            },
        };

    private async Task<HttpResponseMessage> BotAsync(JsonObject update, string? secret = FbsApiFactory.TelegramWebhookSecret)
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Bot") { Content = JsonContent.Create(update) };
        if (secret is not null)
        {
            request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", secret);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> StartAsync(HttpClient client)
    {
        var response = await client.PostAsync("/Me/Telegram/Link", null);
        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var url = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        return url[(url.IndexOf("start=", StringComparison.Ordinal) + "start=".Length)..];
    }

    private static async Task<bool> LinkedAsync(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/Me/Telegram")).GetProperty("linked").GetBoolean();

    private TelegramLink? LinkOf(HttpClient client, Guid accountId) => Factory.Db.Queryable<TelegramLink>().First(l => l.UserId == accountId);

    private string LastMessageTo(long chat) => Factory.Telegram.Messages.Last(m => m.ChatId == chat).Text;

    [Test]
    public async Task Opening_the_link_in_telegram_connects_the_chat_to_the_account()
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Linker");
        var accountId = await Factory.AccountIdOfAsync(client);
        var response = await client.PostAsync("/Me/Telegram/Link", null);
        var made = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = made.GetProperty("url").GetString()!;
        var token = url[(url.IndexOf("start=", StringComparison.Ordinal) + 6)..];

        await Assert.That(url).StartsWith("https://t.me/fbs_test_bot?start=");
        await Assert.That(made.GetProperty("expiresAt").GetDateTimeOffset()).IsLessThan(DateTimeOffset.UtcNow.AddMinutes(11));
        await Assert.That(await LinkedAsync(client)).IsFalse();
        var stored = LinkOf(client, accountId)!;
        await Assert.That(stored.TokenHash).IsNotNull().And.IsNotEqualTo(token);

        var opened = await BotAsync(Update($"/start {token}", chat: 5551));

        await Assert.That(opened).HasStatus(HttpStatusCode.OK);
        await Assert.That(await LinkedAsync(client)).IsTrue();
        var linked = LinkOf(client, accountId)!;
        await Assert.That(linked.ChatId).IsEqualTo("5551");
        await Assert.That(linked.LinkedAt).IsNotNull();
        await Assert.That(linked.TokenHash).IsNull();
        await Assert.That(LastMessageTo(5551)).Contains("Connected to Linker");
    }

    [Test]
    public async Task A_link_works_once()
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Linker");
        var token = await StartAsync(client);
        (await BotAsync(Update($"/start {token}", chat: 5552))).EnsureSuccessStatusCode();

        // The same link, from this chat and from another
        (await BotAsync(Update($"/start {token}", chat: 5552))).EnsureSuccessStatusCode();
        (await BotAsync(Update($"/start {token}", chat: 5553))).EnsureSuccessStatusCode();

        await Assert.That(LastMessageTo(5553)).Contains("expired or has been used");
        var accountId = await Factory.AccountIdOfAsync(client);
        await Assert.That(LinkOf(client, accountId)!.ChatId).IsEqualTo("5552");
    }

    [Test]
    public async Task A_link_that_has_run_out_or_is_made_up_connects_nothing()
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Linker");
        var accountId = await Factory.AccountIdOfAsync(client);
        var token = await StartAsync(client);
        Factory.Db.Updateable<TelegramLink>().SetColumns(l => new TelegramLink { TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-5) }).Where(l => l.UserId == accountId).ExecuteCommand();

        (await BotAsync(Update($"/start {token}", chat: 5554))).EnsureSuccessStatusCode();
        (await BotAsync(Update($"/start {new string('a', 43)}", chat: 5554))).EnsureSuccessStatusCode();
        // Nothing after it
        (await BotAsync(Update("/start   ", chat: 5554))).EnsureSuccessStatusCode();

        await Assert.That(await LinkedAsync(client)).IsFalse();
        await Assert.That(Factory.Telegram.Messages.Count(m => m.ChatId == 5554 && m.Text.Contains("expired or has been used"))).IsEqualTo(3);
        await Assert.That(Factory.Db.Queryable<TelegramLink>().Any(l => l.ChatId == "5554")).IsFalse();
    }

    [Test]
    public async Task Only_a_private_chat_with_the_bot_and_the_bots_secret_can_connect_one()
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Linker");
        var token = await StartAsync(client);

        var inAGroup = await BotAsync(Update($"/start {token}", chat: 5555, chatType: "group"));
        var withoutSecret = await BotAsync(Update($"/start {token}", chat: 5556), secret: null);
        var wrongSecret = await BotAsync(Update($"/start {token}", chat: 5556), secret: "not-the-secret-0123456789");

        await Assert.That(inAGroup).HasStatus(HttpStatusCode.OK);
        await Assert.That(withoutSecret).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(wrongSecret).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await LinkedAsync(client)).IsFalse();
        // And the link is still good for the private chat
        (await BotAsync(Update($"/start {token}", chat: 5557))).EnsureSuccessStatusCode();
        await Assert.That(await LinkedAsync(client)).IsTrue();
    }

    [Test]
    public async Task A_new_link_replaces_one_that_was_not_used_and_leaves_the_chat_that_is_connected_until_it_is_opened()
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Linker");
        var accountId = await Factory.AccountIdOfAsync(client);
        var first = await StartAsync(client);
        (await BotAsync(Update($"/start {first}", chat: 5558))).EnsureSuccessStatusCode();

        var old = await StartAsync(client);
        var replacement = await StartAsync(client);
        await Assert.That(LinkOf(client, accountId)!.ChatId).IsEqualTo("5558");
        (await BotAsync(Update($"/start {old}", chat: 5559))).EnsureSuccessStatusCode();
        await Assert.That(LinkOf(client, accountId)!.ChatId).IsEqualTo("5558");
        (await BotAsync(Update($"/start {replacement}", chat: 5559))).EnsureSuccessStatusCode();

        await Assert.That(LinkOf(client, accountId)!.ChatId).IsEqualTo("5559");
        await Assert.That(Factory.Db.Queryable<TelegramLink>().Count(l => l.UserId == accountId)).IsEqualTo(1);
    }

    [Test]
    public async Task A_chat_belongs_to_one_account_and_connecting_it_to_another_takes_it_from_the_first()
    {
        var first = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "First");
        var second = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Second");
        (await BotAsync(Update($"/start {await StartAsync(first)}", chat: 5560))).EnsureSuccessStatusCode();

        (await BotAsync(Update($"/start {await StartAsync(second)}", chat: 5560))).EnsureSuccessStatusCode();

        await Assert.That(await LinkedAsync(first)).IsFalse();
        await Assert.That(await LinkedAsync(second)).IsTrue();
        await Assert.That(Factory.Db.Queryable<TelegramLink>().Count(l => l.ChatId == "5560")).IsEqualTo(1);
    }

    [Test]
    public async Task Disconnecting_stops_the_chat_and_a_link_that_was_made_and_not_used()
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Linker");
        (await BotAsync(Update($"/start {await StartAsync(client)}", chat: 5561))).EnsureSuccessStatusCode();
        var pending = await StartAsync(client);

        var response = await client.DeleteAsync("/Me/Telegram");
        (await BotAsync(Update($"/start {pending}", chat: 5562))).EnsureSuccessStatusCode();

        await Assert.That(response).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(await LinkedAsync(client)).IsFalse();
        await Assert.That(Factory.Db.Queryable<TelegramLink>().Any(l => l.ChatId == "5562")).IsFalse();
        // And doing it when nothing is connected is fine
        await Assert.That(await client.DeleteAsync("/Me/Telegram")).HasStatus(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Without_signing_in_there_is_nothing_to_connect()
    {
        using var nobody = Factory.CreateClient();

        await Assert.That(await nobody.PostAsync("/Me/Telegram/Link", null)).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await nobody.GetAsync("/Me/Telegram")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await nobody.DeleteAsync("/Me/Telegram")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Whoever_starts_it_at_the_same_moment_still_has_one_row_and_a_link_that_works()
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Linker");
        var accountId = await Factory.AccountIdOfAsync(client);

        List<Task<HttpResponseMessage>> requests;
        using (ExecutionContext.SuppressFlow())
        {
            requests = Enumerable.Range(0, 6).Select(_ => Task.Run(() => client.PostAsync("/Me/Telegram/Link", null))).ToList();
        }

        var responses = await Task.WhenAll(requests);

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.OK]);
        await Assert.That(Factory.Db.Queryable<TelegramLink>().Count(l => l.UserId == accountId)).IsEqualTo(1);
    }

    [Test]
    public async Task The_bot_is_asked_what_it_is_called_once_and_the_configured_name_is_used_without_asking()
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        var before = Factory.Telegram.GetMeCalls;
        await StartAsync(client);
        await StartAsync(client);
        await Assert.That(Factory.Telegram.GetMeCalls - before).IsLessThanOrEqualTo(1);

        await using var configured = new ConfiguredBotFactory();
        var other = configured.ClientFor(ClerkFbsApiFactory.NewUserId());
        var url = (await (await other.PostAsync("/Me/Telegram/Link", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;

        await Assert.That(url).StartsWith("https://t.me/configured_bot?start=");
        await Assert.That(configured.Telegram.GetMeCalls).IsEqualTo(0);
    }

    private sealed class ConfiguredBotFactory : ClerkFbsApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Telegram:BotUsername", "@configured_bot");
        }
    }

    // What the outbox tells people, in the organisation the factory's API works for
    private async Task<HttpClient> SignedInAsMemberAsync(string phone, string? chat = null)
    {
        var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), phone);
        var accountId = await Factory.AccountIdOfAsync(client);
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { UserId = accountId }).Where(m => m.Id == Factory.MemberIdOf(phone)).ExecuteCommand();
        if (chat is not null)
        {
            Factory.Db.Insertable(new TelegramLink { Id = Guid.NewGuid(), UserId = accountId, ChatId = chat, LinkedAt = DateTimeOffset.UtcNow }).ExecuteCommand();
        }

        return client;
    }

    private static object Book(Guid facility) =>
        new
        {
            conduct = "Lesson",
            slots = new[]
            {
                new
                {
                    facilityId = facility,
                    startDateTime = new DateTimeOffset(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore")).Date.AddDays(4).AddHours(9), TimeSpan.FromHours(8)),
                    endDateTime = new DateTimeOffset(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore")).Date.AddDays(4).AddHours(10), TimeSpan.FromHours(8)),
                },
            },
        };

    private Guid Eiger => Factory.Db.Queryable<DataFacility>().First(f => f.TenantId == Factory.TenantId && f.Name == "Eiger").Id;

    [Test]
    public async Task Bookings_are_told_in_the_chat_the_account_connected_and_otherwise_the_one_a_phone_number_was_linked_to()
    {
        // The booker has no chat connected to their account, so it is the one their number had. Everyone's has
        // one connected, so that is where it goes rather than to the one their number had
        var booker = await SignedInAsMemberAsync(Users.Booker);
        await SignedInAsMemberAsync(Users.AllGroup, chat: "9100");

        var response = await booker.PostAsJsonAsync($"/t/{Factory.Slug}/Bookings", Book(Eiger));

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        // The booker (1001), their unit (1002 has the unit scope), and everyone's, who is in 9100
        var messages = await Factory.Telegram.WaitForMessagesAsync(3);
        await Assert.That(messages.Select(m => m.ChatId)).IsEquivalentTo([1001L, 1002L, 9100L]);
    }

    [Test]
    public async Task Someone_in_several_organisations_is_told_which_one_a_booking_is_in()
    {
        var booker = await SignedInAsMemberAsync(Users.Booker);
        var everyone = await SignedInAsMemberAsync(Users.AllGroup, chat: "9101");
        // Also in another organisation
        var other = await Factory.CreateOrgAsync();
        var accountId = await Factory.AccountIdOfAsync(everyone);
        Factory.Db.Insertable(new TenantMember { Id = Guid.NewGuid(), TenantId = other.TenantId, UserId = accountId, DisplayName = "Elsewhere", Status = MemberStatus.Active }).ExecuteCommand();

        (await booker.PostAsJsonAsync($"/t/{Factory.Slug}/Bookings", Book(Eiger))).EnsureSuccessStatusCode();

        var messages = await Factory.Telegram.WaitForMessagesAsync(3);
        await Assert.That(messages.Single(m => m.ChatId == 9101).Text).StartsWith("<b>Test</b>\n");
        await Assert.That(messages.Single(m => m.ChatId == 1001).Text).DoesNotContain("<b>Test</b>");
    }

    [Test]
    public async Task Someone_who_is_waiting_to_be_let_in_or_has_left_is_told_nothing()
    {
        var booker = await SignedInAsMemberAsync(Users.Booker);
        await SignedInAsMemberAsync(Users.AllGroup, chat: "9102");
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { Status = MemberStatus.Pending }).Where(m => m.Id == Factory.MemberIdOf(Users.AllGroup)).ExecuteCommand();
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { Status = MemberStatus.Removed }).Where(m => m.Id == Factory.MemberIdOf(Users.SameUnit)).ExecuteCommand();

        (await booker.PostAsJsonAsync($"/t/{Factory.Slug}/Bookings", Book(Eiger))).EnsureSuccessStatusCode();

        var messages = await Factory.Telegram.WaitForMessagesAsync(1);
        await Task.Delay(1500);
        await Assert.That(Factory.Telegram.Messages.Select(m => m.ChatId)).IsEquivalentTo([1001L]);
    }
}
