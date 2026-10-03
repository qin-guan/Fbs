using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Fbs.WebApi.Tests.Data;

namespace Fbs.WebApi.Tests.Helpers;

/// <summary>Updates as Telegram sends them to the bot's webhook.</summary>
public static class BotUpdates
{
    /// <summary>A message with text, sent from a chat with the sender.</summary>
    public static JsonObject Text(string text, long chat = 5551, string chatType = "private") =>
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

    /// <summary>Sends the update to the bot the way Telegram does, with the secret it was given.</summary>
    public static async Task<HttpResponseMessage> PostToBotAsync(this FbsApiFactory factory, JsonObject update, string? secret = FbsApiFactory.TelegramWebhookSecret)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Bot") { Content = JsonContent.Create(update) };
        if (secret is not null)
        {
            request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", secret);
        }

        return await client.SendAsync(request);
    }
}
