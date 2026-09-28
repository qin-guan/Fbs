using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fbs.WebApi.Tests.Fakes;

/// <summary>
/// Stand-in for the Telegram Bot API that records every message sent.
/// </summary>
public class FakeTelegram
{
    private int _messageId;

    public ConcurrentQueue<(long ChatId, string Text)> Messages { get; } = new();

    public HttpMessageHandler CreateHandler() => new Handler(this);

    public async Task<IReadOnlyList<(long ChatId, string Text)>> WaitForMessagesAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Messages.Count < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        return Messages.ToList();
    }

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var method = request.RequestUri!.Segments[^1].ToLowerInvariant();
        var body = request.Content is null
            ? null
            : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct));

        switch (method)
        {
            case "setwebhook":
                return Ok(true);

            case "sendmessage":
                var chatNode = body!["chat_id"]!;
                var chatId = chatNode.GetValueKind() == JsonValueKind.String
                    ? long.Parse(chatNode.GetValue<string>())
                    : chatNode.GetValue<long>();
                var text = body["text"]!.GetValue<string>();
                Messages.Enqueue((chatId, text));

                return Ok(
                    new JsonObject
                    {
                        ["message_id"] = Interlocked.Increment(ref _messageId),
                        ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ["chat"] = new JsonObject { ["id"] = chatId, ["type"] = "private" },
                        ["text"] = text,
                    }
                );

            default:
                return new HttpResponseMessage(HttpStatusCode.NotImplemented);
        }
    }

    private static HttpResponseMessage Ok(JsonNode result) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JsonObject { ["ok"] = true, ["result"] = result }.ToJsonString(),
                Encoding.UTF8,
                "application/json"
            ),
        };

    private sealed class Handler(FakeTelegram telegram) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            telegram.HandleAsync(request, ct);
    }
}
