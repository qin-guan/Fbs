using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

public class BotTests
{
    private const long OwnChatId = 9001;
    private const long SomeoneElsesId = 7777;

    [ClassDataSource<FbsApiFactory>]
    public required FbsApiFactory Factory { get; init; }

    /// <summary>What the Users sheet holds as the booker's Telegram chat ID.</summary>
    private string BookerChatId => Factory.Google.Sheets["Users"].Single(row => row[2] == Users.Booker)[3];

    /// <summary>
    /// A Telegram update in which <paramref name="sender"/> shares the booker's number from a chat.
    /// </summary>
    private static JsonObject ContactUpdate(
        long sender = OwnChatId,
        long? contactUserId = OwnChatId,
        string chatType = "private"
    )
    {
        var contact = new JsonObject { ["phone_number"] = $"+{Users.Booker}", ["first_name"] = "Booker" };
        if (contactUserId is { } userId)
        {
            contact["user_id"] = userId;
        }

        return new JsonObject
        {
            ["update_id"] = 1,
            ["message"] = new JsonObject
            {
                ["message_id"] = 1,
                ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["chat"] = new JsonObject { ["id"] = sender, ["type"] = chatType },
                ["from"] = new JsonObject { ["id"] = sender, ["is_bot"] = false, ["first_name"] = "Sender" },
                ["contact"] = contact,
            },
        };
    }

    private async Task<HttpResponseMessage> PostAsync(JsonObject update, string? secret = FbsApiFactory.TelegramWebhookSecret)
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Bot") { Content = JsonContent.Create(update) };
        if (secret is not null)
        {
            request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", secret);
        }

        return await client.SendAsync(request);
    }

    [Test]
    public async Task The_webhook_is_registered_with_the_secret_token()
    {
        using var _ = Factory.CreateClient();

        var registration = await Assert.That(Factory.Telegram.WebhookRegistrations).HasSingleItem();

        await Assert.That(registration["url"]!.GetValue<string>()).IsEqualTo("https://fbs.test/Bot");
        await Assert.That(registration["secret_token"]!.GetValue<string>()).IsEqualTo(FbsApiFactory.TelegramWebhookSecret);
    }

    [Test]
    public async Task Sharing_your_own_contact_links_the_chat()
    {
        var response = await PostAsync(ContactUpdate());

        response.EnsureSuccessStatusCode();
        await Assert.That(BookerChatId).IsEqualTo(OwnChatId.ToString());
        var messages = await Factory.Telegram.WaitForMessagesAsync(1);
        await Assert.That(messages[0].ChatId).IsEqualTo(OwnChatId);
        await Assert.That(messages[0].Text).Contains("successfully registered");
    }

    [Test]
    public async Task Updates_without_the_secret_token_are_rejected()
    {
        var before = BookerChatId;

        var response = await PostAsync(ContactUpdate(), secret: null);

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(BookerChatId).IsEqualTo(before);
        await Assert.That(Factory.Telegram.Messages).IsEmpty();
    }

    [Test]
    public async Task Updates_with_the_wrong_secret_token_are_rejected()
    {
        var before = BookerChatId;

        var response = await PostAsync(ContactUpdate(), secret: "not-the-secret-0123456789");

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(BookerChatId).IsEqualTo(before);
        await Assert.That(Factory.Telegram.Messages).IsEmpty();
    }

    [Test]
    public async Task Someone_elses_contact_cannot_be_linked()
    {
        var before = BookerChatId;

        // The sender shares a contact that belongs to a different Telegram user
        var response = await PostAsync(ContactUpdate(contactUserId: SomeoneElsesId));

        response.EnsureSuccessStatusCode();
        await Assert.That(BookerChatId).IsEqualTo(before);
        var messages = await Factory.Telegram.WaitForMessagesAsync(1);
        await Assert.That(messages[0].ChatId).IsEqualTo(OwnChatId);
        await Assert.That(messages[0].Text).Contains("share your own phone number");
    }

    [Test]
    public async Task A_contact_that_is_only_a_phone_number_cannot_be_linked()
    {
        var before = BookerChatId;

        // A contact typed into an address book isn't tied to a Telegram user, so it has no user ID
        var response = await PostAsync(ContactUpdate(contactUserId: null));

        response.EnsureSuccessStatusCode();
        await Assert.That(BookerChatId).IsEqualTo(before);
    }

    [Test]
    public async Task Contacts_shared_in_group_chats_cannot_be_linked()
    {
        var before = BookerChatId;

        var response = await PostAsync(ContactUpdate(chatType: "group"));

        response.EnsureSuccessStatusCode();
        await Assert.That(BookerChatId).IsEqualTo(before);
    }
}
