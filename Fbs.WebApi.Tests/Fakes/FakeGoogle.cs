using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;
using Fbs.WebApi.Entities;
using Google.Apis.Http;
using MemoryPack;

namespace Fbs.WebApi.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the Google Sheets and Calendar REST APIs used by the repositories.
/// </summary>
/// <remarks>
/// Event lists are paged and support incremental sync like the real API, see
/// https://developers.google.com/workspace/calendar/api/guides/sync
/// </remarks>
public partial class FakeGoogle
{
    public const string MainCalendar = "main-calendar";
    public const string CarbonCopyCalendar = "carbon-copy-calendar";

    /// <summary>The page size the real API uses when maxResults isn't given.</summary>
    private const int DefaultPageSize = 250;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Dictionary<string, StoredEvent>> _calendars = new();
    private readonly Dictionary<string, (List<JsonObject> Items, int Offset, long Version)> _pages =
        new();
    private int _inserts;
    private long _version;
    private long _oldestValidSyncToken;
    private long _sequence;

    public Dictionary<string, List<List<string>>> Sheets { get; } = new()
    {
        ["Users"] =
        [
            ["Unit", "Name", "Phone", "TelegramChatId", "NotificationGroup", "IsAdmin"],
            ["Alpha", "CPT Booker", Users.Booker, "1001", "Unit", "FALSE"],
            ["Alpha", "LTA Same Unit", Users.SameUnit, "1002", "Unit", "FALSE"],
            ["Bravo", "3SG Everyone", Users.AllGroup, "1003", "All", "FALSE"],
            ["Bravo", "PTE Other Unit", Users.OtherUnit, "1004", "Unit", "FALSE"],
            ["Charlie", "MAJ Admin", Users.Admin, "1005", "None", "TRUE"],
        ],
        ["Facilities"] =
        [
            ["Name", "Group", "Scope"],
            ["Eiger", "Parade Square", "All"],
            ["Field", "Sports", "Alpha, Bravo"],
            ["Gym", "Sports", "Bravo"],
        ],
        ["OTPs"] = [["Phone", "Code", "CreatedAt"]],
    };

    /// <summary>
    /// Makes the n-th event insert (1-based, counted across both calendars) fail with a 500.
    /// </summary>
    public int? FailInsertNumber { get; set; }

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
    /// Adds or replaces a booking's events in both calendars, as if it was changed outside the API.
    /// </summary>
    public void AddBooking(Booking booking)
    {
        var data = Convert.ToBase64String(MemoryPackSerializer.Serialize(booking));
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
                        ["extendedProperties"] = new JsonObject
                        {
                            ["shared"] = new JsonObject { ["Data"] = data },
                        },
                    }
                );
            }
        }
    }

    /// <summary>Removes a booking's events from both calendars, as if it was deleted outside the API.</summary>
    public void RemoveBooking(Guid id)
    {
        lock (_lock)
        {
            foreach (var calendarId in new[] { MainCalendar, CarbonCopyCalendar })
            {
                Delete(calendarId, id.ToString("N"));
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

    /// <summary>Makes every sync token issued so far invalid, so the next incremental sync gets a 410.</summary>
    public void ExpireSyncTokens()
    {
        lock (_lock)
        {
            _oldestValidSyncToken = _version + 1;
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

    private void Store(string calendarId, JsonObject body)
    {
        var calendar = Calendar(calendarId);
        var id = body["id"]!.GetValue<string>();
        var sequence = calendar.TryGetValue(id, out var existing) ? existing.Sequence : ++_sequence;
        calendar[id] = new StoredEvent(body, ++_version, false, sequence);
    }

    private bool Delete(string calendarId, string eventId)
    {
        var calendar = Calendar(calendarId);
        if (!calendar.TryGetValue(eventId, out var existing) || existing.Deleted)
        {
            return false;
        }

        calendar[eventId] = existing with { Version = ++_version, Deleted = true };
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
            return Handle(request.Method, segments, query, body);
        }
    }

    private HttpResponseMessage Handle(
        HttpMethod method,
        string[] segments,
        System.Collections.Specialized.NameValueCollection query,
        string? body
    )
    {
        // GET /v4/spreadsheets/{id}
        if (segments is ["v4", "spreadsheets", _] && method == HttpMethod.Get)
        {
            var sheets = new JsonArray(
                Sheets
                    .Keys.Select(title =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["properties"] = new JsonObject
                                {
                                    ["sheetId"] = SheetId(title),
                                    ["title"] = title,
                                },
                            }
                    )
                    .ToArray()
            );
            return Json(HttpStatusCode.OK, new JsonObject { ["sheets"] = sheets });
        }

        // POST /v4/spreadsheets/{id}:batchUpdate
        if (segments is ["v4", "spreadsheets", var batch] && batch.EndsWith(":batchUpdate"))
        {
            foreach (var r in JsonNode.Parse(body!)!["requests"]!.AsArray())
            {
                var range = r!["deleteDimension"]!["range"]!;
                var sheet = Sheets.Single(s => SheetId(s.Key) == range["sheetId"]!.GetValue<int>());
                var start = range["startIndex"]!.GetValue<int>();
                var end = range["endIndex"]!.GetValue<int>();
                sheet.Value.RemoveRange(start, end - start);
            }

            return Json(HttpStatusCode.OK, new JsonObject { ["replies"] = new JsonArray() });
        }

        // POST /v4/spreadsheets/{id}/values/{sheet}:append
        if (
            segments is ["v4", "spreadsheets", _, "values", var append]
            && append.EndsWith(":append")
            && method == HttpMethod.Post
        )
        {
            var title = append[..^":append".Length];
            var sheet = Sheets[title];
            var rows = Rows(body!);
            var first = sheet.Count + 1;
            sheet.AddRange(rows);
            var last = sheet.Count;
            return Json(
                HttpStatusCode.OK,
                new JsonObject
                {
                    ["updates"] = new JsonObject
                    {
                        ["updatedRange"] = $"{title}!A{first}:{ColumnName(rows[0].Count)}{last}",
                        ["updatedRows"] = rows.Count,
                    },
                }
            );
        }

        // PUT /v4/spreadsheets/{id}/values/{sheet}!A{row}:F{row}
        if (segments is ["v4", "spreadsheets", _, "values", var update] && method == HttpMethod.Put)
        {
            var match = A1Range().Match(update);
            var sheet = Sheets[match.Groups["sheet"].Value];
            var row = int.Parse(match.Groups["row"].Value);
            foreach (var values in Rows(body!))
            {
                sheet[row - 1] = values;
                row++;
            }

            return Json(HttpStatusCode.OK, new JsonObject { ["updatedRange"] = update });
        }

        // GET /v4/spreadsheets/{id}/values/{range}
        if (segments is ["v4", "spreadsheets", _, "values", var get] && method == HttpMethod.Get)
        {
            var values = new JsonArray(
                Sheets[get]
                    .Select(row =>
                        (JsonNode)
                            new JsonArray(
                                // Like the real API, trailing empty cells are left out
                                row.Take(row.FindLastIndex(v => v != "") + 1)
                                    .Select(v => (JsonNode)v!)
                                    .ToArray()
                            )
                    )
                    .ToArray()
            );
            return Json(
                HttpStatusCode.OK,
                new JsonObject
                {
                    ["range"] = get,
                    ["majorDimension"] = "ROWS",
                    ["values"] = values,
                }
            );
        }

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

            if (eventId is null && method == HttpMethod.Get)
            {
                return List(calendar, query);
            }

            if (eventId is null && method == HttpMethod.Post)
            {
                if (Interlocked.Increment(ref _inserts) == FailInsertNumber)
                {
                    return Error(HttpStatusCode.InternalServerError, "Backend Error");
                }

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

    private HttpResponseMessage List(
        Dictionary<string, StoredEvent> calendar,
        System.Collections.Specialized.NameValueCollection query
    )
    {
        List<JsonObject> items;
        int offset;
        long version;

        if (query["pageToken"] is { } pageToken)
        {
            (items, offset, version) = _pages[pageToken];
            _pages.Remove(pageToken);
        }
        else if (query["syncToken"] is { } syncToken)
        {
            var since = long.Parse(syncToken);
            if (since < _oldestValidSyncToken)
            {
                return Error(HttpStatusCode.Gone, "Sync token is no longer valid, a full sync is required.");
            }

            // Changes since the token, including deletions
            items = calendar
                .Values.Where(e => e.Version > since)
                .OrderBy(e => e.Version)
                .Select(e => e.ToJson())
                .ToList();
            offset = 0;
            version = _version;
        }
        else
        {
            items = calendar
                .Values.Where(e => !e.Deleted)
                .OrderBy(e => e.Sequence)
                .Select(e => e.ToJson())
                .ToList();
            offset = 0;
            version = _version;
        }

        var pageSize = query["maxResults"] is { } maxResults
            ? Math.Min(int.Parse(maxResults), 2500)
            : DefaultPageSize;
        var page = items.Skip(offset).Take(pageSize).Select(e => (JsonNode)e).ToArray();

        var response = new JsonObject { ["kind"] = "calendar#events", ["items"] = new JsonArray(page) };
        if (offset + pageSize < items.Count)
        {
            var next = Guid.NewGuid().ToString("N");
            _pages[next] = (items, offset + pageSize, version);
            response["nextPageToken"] = next;
        }
        else
        {
            response["nextSyncToken"] = version.ToString();
        }

        return Json(HttpStatusCode.OK, response);
    }

    private static int SheetId(string title) => Math.Abs(title.GetHashCode() % 100_000);

    private static List<List<string>> Rows(string body) =>
        JsonNode
            .Parse(body)!["values"]!.AsArray()
            .Select(row => row!.AsArray().Select(v => v?.GetValue<string>() ?? "").ToList())
            .ToList();

    private static string ColumnName(int count) => ((char)('A' + count - 1)).ToString();

    [GeneratedRegex(@"^(?<sheet>[^!]+)!A(?<row>\d+)")]
    private static partial Regex A1Range();

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
    )
    {
        public bool IsEventList(string calendarId) =>
            Method == HttpMethod.Get && Path == $"calendar/v3/calendars/{calendarId}/events";

        /// <summary>The first page of a list of every event in a calendar, rather than of recent changes.</summary>
        public bool IsFullEventList(string calendarId) =>
            IsEventList(calendarId) && Query["syncToken"] is null && Query["pageToken"] is null;
    }

    private sealed record StoredEvent(JsonObject Body, long Version, bool Deleted, long Sequence)
    {
        /// <summary>Deleted events only keep their id and status, like the real API.</summary>
        public JsonObject ToJson() =>
            Deleted
                ? new JsonObject { ["id"] = Body["id"]!.DeepClone(), ["status"] = "cancelled" }
                : Body.DeepClone().AsObject();
    }

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
