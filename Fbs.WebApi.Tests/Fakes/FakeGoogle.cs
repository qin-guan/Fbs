using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Fbs.WebApi.Entities;
using Google.Apis.Http;
using MemoryPack;

namespace Fbs.WebApi.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the Google Sheets and Calendar REST APIs used by the repositories.
/// </summary>
public class FakeGoogle
{
    public const string MainCalendar = "main-calendar";
    public const string CarbonCopyCalendar = "carbon-copy-calendar";

    private int _inserts;

    public Dictionary<string, List<List<string>>> Sheets { get; } = new()
    {
        ["Users"] =
        [
            ["Unit", "Name", "Phone", "TelegramChatId", "NotificationGroup", "IsAdmin"],
            ["Alpha", "CPT Booker", Users.Booker, "1001", "Unit", "FALSE"],
            ["Alpha", "LTA Same Unit", Users.SameUnit, "1002", "Unit", "FALSE"],
            ["Bravo", "3SG Everyone", Users.AllGroup, "1003", "All", "FALSE"],
            ["Bravo", "PTE Other Unit", Users.OtherUnit, "1004", "Unit", "FALSE"],
        ],
        ["Facilities"] =
        [
            ["Name", "Group", "Scope"],
            ["Eiger", "Parade Square", "All"],
            ["Field", "Sports", "Alpha, Bravo"],
            ["Gym", "Sports", "Bravo"],
        ],
    };

    public ConcurrentDictionary<string, ConcurrentDictionary<string, JsonObject>> Calendars { get; } =
        new();

    /// <summary>
    /// Makes the n-th event insert (1-based, counted across both calendars) fail with a 500.
    /// </summary>
    public int? FailInsertNumber { get; set; }

    public IReadOnlyList<JsonObject> Events(string calendarId) =>
        Calendar(calendarId).Values.ToList();

    public void AddBooking(Booking booking)
    {
        var data = Convert.ToBase64String(MemoryPackSerializer.Serialize(booking));
        foreach (var calendarId in new[] { MainCalendar, CarbonCopyCalendar })
        {
            Calendar(calendarId)[booking.Id.ToString("N")] = new JsonObject
            {
                ["id"] = booking.Id.ToString("N"),
                ["start"] = new JsonObject { ["dateTime"] = Rfc3339(booking.StartDateTime!.Value) },
                ["end"] = new JsonObject { ["dateTime"] = Rfc3339(booking.EndDateTime!.Value) },
                ["extendedProperties"] = new JsonObject
                {
                    ["shared"] = new JsonObject { ["Data"] = data },
                },
            };
        }
    }

    public Google.Apis.Http.IHttpClientFactory CreateHttpClientFactory() => new Factory(this);

    private ConcurrentDictionary<string, JsonObject> Calendar(string calendarId) =>
        Calendars.GetOrAdd(calendarId, _ => new ConcurrentDictionary<string, JsonObject>());

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var segments = request
            .RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        // GET /v4/spreadsheets/{id}/values/{range}
        if (segments is ["v4", "spreadsheets", _, "values", var range] && request.Method == HttpMethod.Get)
        {
            var values = new JsonArray(
                Sheets[range].Select(row => (JsonNode)new JsonArray(row.Select(v => (JsonNode)v!).ToArray())).ToArray()
            );
            return Json(HttpStatusCode.OK, new JsonObject { ["range"] = range, ["majorDimension"] = "ROWS", ["values"] = values });
        }

        // /calendar/v3/calendars/{calendarId}/events[/{eventId}]
        if (segments is ["calendar", "v3", "calendars", var calendarId, "events", ..])
        {
            var calendar = Calendar(calendarId);
            var eventId = segments.Length > 5 ? segments[5] : null;

            if (eventId is null && request.Method == HttpMethod.Get)
            {
                var items = new JsonArray(calendar.Values.Select(e => e.DeepClone()).ToArray());
                return Json(HttpStatusCode.OK, new JsonObject { ["kind"] = "calendar#events", ["items"] = items });
            }

            if (eventId is null && request.Method == HttpMethod.Post)
            {
                if (Interlocked.Increment(ref _inserts) == FailInsertNumber)
                {
                    return Error(HttpStatusCode.InternalServerError, "Backend Error");
                }

                var body = JsonNode.Parse(await ReadBodyAsync(request, ct))!.AsObject();
                calendar[body["id"]!.GetValue<string>()] = body;
                return Json(HttpStatusCode.OK, body.DeepClone());
            }

            if (eventId is not null && request.Method == HttpMethod.Delete)
            {
                return calendar.TryRemove(eventId, out _)
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : Error(HttpStatusCode.NotFound, "Not Found");
            }
        }

        return Error(HttpStatusCode.NotImplemented, $"{request.Method} {request.RequestUri} is not faked");
    }

    private static string Rfc3339(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz");

    /// <summary>The Google client gzips request bodies.</summary>
    private static async Task<string> ReadBodyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var stream = await request.Content!.ReadAsStreamAsync(ct);
        if (request.Content.Headers.ContentEncoding.Contains("gzip"))
        {
            stream = new GZipStream(stream, CompressionMode.Decompress);
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string message) =>
        Json(status, new JsonObject { ["error"] = new JsonObject { ["code"] = (int)status, ["message"] = message } });

    private sealed class Factory(FakeGoogle google) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new Handler(google);
    }

    private sealed class Handler(FakeGoogle google) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            google.HandleAsync(request, ct);
    }
}
