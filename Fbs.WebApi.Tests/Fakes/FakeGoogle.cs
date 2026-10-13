using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Fbs.WebApi.Entities;
using Google.Apis.Http;

namespace Fbs.WebApi.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the Google Calendar REST API, which bookings are copied to.
/// </summary>
public class FakeGoogle
{
    public const string MainCalendar = "main-calendar";
    public const string CarbonCopyCalendar = "carbon-copy-calendar";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Dictionary<string, StoredEvent>> _calendars = new();

    /// <summary>Calendars that answer every request as if Google had a problem, so it is worth trying again.</summary>
    public HashSet<string> FailingCalendars { get; } = [];

    /// <summary>Calendars that answer every request as if they weren't shared with us, which trying again won't change.</summary>
    public HashSet<string> ForbiddenCalendars { get; } = [];

    /// <summary>Calendars that answer every request as if we were asking too often.</summary>
    public HashSet<string> RateLimitedCalendars { get; } = [];

    /// <summary>
    /// How long each request takes, to stand in for the round trip to Google.
    /// </summary>
    public TimeSpan Latency { get; set; }

    /// <summary>Every request received, oldest first.</summary>
    public ConcurrentQueue<Request> Requests { get; } = new();

    public IReadOnlyList<JsonObject> Events(string calendarId)
    {
        lock (_lock)
        {
            return Calendar(calendarId)
                .Values.Where(e => !e.Deleted)
                .Select(e => e.Body.DeepClone().AsObject())
                .ToList();
        }
    }

    /// <summary>
    /// Adds or replaces a booking's events in both calendars, as the version before the database kept them there.
    /// </summary>
    public void AddBooking(Booking booking)
    {
        lock (_lock)
        {
            foreach (var calendarId in new[] { MainCalendar, CarbonCopyCalendar })
            {
                Store(
                    calendarId,
                    new JsonObject
                    {
                        ["id"] = booking.Id.ToString("N"),
                        ["status"] = "confirmed",
                        ["summary"] = booking.Conduct,
                        ["start"] = new JsonObject
                        {
                            ["dateTime"] = Rfc3339(booking.StartDateTime!.Value),
                        },
                        ["end"] = new JsonObject
                        {
                            ["dateTime"] = Rfc3339(booking.EndDateTime!.Value),
                        },
                    }
                );
            }
        }
    }

    /// <summary>Removes a booking's event from one calendar, as if it was deleted by hand there.</summary>
    public void RemoveEvent(string calendarId, Guid id)
    {
        lock (_lock)
        {
            Delete(calendarId, id.ToString("N"));
        }
    }

    public Google.Apis.Http.IHttpClientFactory CreateHttpClientFactory() => new Factory(this);

    private Dictionary<string, StoredEvent> Calendar(string calendarId)
    {
        if (!_calendars.TryGetValue(calendarId, out var calendar))
        {
            _calendars[calendarId] = calendar = new Dictionary<string, StoredEvent>();
        }

        return calendar;
    }

    private void Store(string calendarId, JsonObject body) =>
        Calendar(calendarId)[body["id"]!.GetValue<string>()] = new StoredEvent(body, false);

    private bool Delete(string calendarId, string eventId)
    {
        var calendar = Calendar(calendarId);
        if (!calendar.TryGetValue(eventId, out var existing) || existing.Deleted)
        {
            return false;
        }

        calendar[eventId] = existing with { Deleted = true };
        return true;
    }

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var segments = request
            .RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        Requests.Enqueue(new Request(request.Method, string.Join('/', segments), query));

        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency, ct);
        }

        var body = request.Content is null ? null : await ReadBodyAsync(request, ct);
        lock (_lock)
        {
            return Handle(request.Method, segments, body);
        }
    }

    private HttpResponseMessage Handle(HttpMethod method, string[] segments, string? body)
    {
        // /calendar/v3/calendars/{calendarId}/events[/{eventId}]
        if (segments is ["calendar", "v3", "calendars", var calendarId, "events", ..])
        {
            if (FailingCalendars.Contains(calendarId))
            {
                return Error(HttpStatusCode.InternalServerError, "Backend Error", "backendError");
            }

            if (ForbiddenCalendars.Contains(calendarId))
            {
                return Error(HttpStatusCode.Forbidden, "Forbidden", "forbidden");
            }

            if (RateLimitedCalendars.Contains(calendarId))
            {
                return Error(HttpStatusCode.Forbidden, "Rate Limit Exceeded", "rateLimitExceeded");
            }

            var calendar = Calendar(calendarId);
            var eventId = segments.Length > 5 ? segments[5] : null;

            if (eventId is null && method == HttpMethod.Post)
            {
                var @event = JsonNode.Parse(body!)!.AsObject();
                // As with the real API, an ID that has been used can't be inserted again, even if what used it was deleted
                if (@event["id"]?.GetValue<string>() is { } insertedId && calendar.ContainsKey(insertedId))
                {
                    return Error(HttpStatusCode.Conflict, "The requested identifier already exists.", "duplicate");
                }

                @event["status"] = "confirmed";
                Store(calendarId, @event);
                return Json(HttpStatusCode.OK, @event.DeepClone());
            }

            if (eventId is not null && method == HttpMethod.Put)
            {
                if (!calendar.TryGetValue(eventId, out var existing))
                {
                    return Error(HttpStatusCode.NotFound, "Not Found");
                }

                // What was deleted can't be found, or brought back with an update that says it is confirmed
                var @event = JsonNode.Parse(body!)!.AsObject();
                if (existing.Deleted && @event["status"]?.GetValue<string>() != "confirmed")
                {
                    return Error(HttpStatusCode.NotFound, "Not Found");
                }

                @event["status"] = "confirmed";
                Store(calendarId, @event);
                return Json(HttpStatusCode.OK, @event.DeepClone());
            }

            if (eventId is not null && method == HttpMethod.Delete)
            {
                if (calendar.TryGetValue(eventId, out var existing) && existing.Deleted)
                {
                    return Error(HttpStatusCode.Gone, "Resource has been deleted");
                }

                return Delete(calendarId, eventId)
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : Error(HttpStatusCode.NotFound, "Not Found");
            }
        }

        return Error(HttpStatusCode.NotImplemented, $"{method} {string.Join('/', segments)} is not faked");
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

    private static HttpResponseMessage Error(HttpStatusCode status, string message, string? reason = null) =>
        Json(
            status,
            new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = (int)status,
                    ["message"] = message,
                    ["errors"] = new JsonArray(new JsonObject { ["message"] = message, ["reason"] = reason ?? "error" }),
                },
            }
        );

    public record Request(
        HttpMethod Method,
        string Path,
        System.Collections.Specialized.NameValueCollection Query
    );

    private sealed record StoredEvent(JsonObject Body, bool Deleted);

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
