#:package FreeSql@3.5.306
#:package FreeSql.Provider.Sqlite@3.5.306
#:package Google.Apis.Calendar.v3@1.73.0.4063
#:package Google.Apis.Auth@1.73.0
#:package MemoryPack@1.21.4

using System.Text;
using FreeSql.DataAnnotations;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using MemoryPack;

// ── Configuration ──────────────────────────────────────────────────────────────
// Set these values before running:

var googleServiceAccountJsonBase64 = Environment.GetEnvironmentVariable("GOOGLE_SERVICE_ACCOUNT_JSON_BASE64")
    ?? throw new Exception("Set GOOGLE_SERVICE_ACCOUNT_JSON_BASE64 environment variable");

var googleCalendarId = Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_ID")
    ?? throw new Exception("Set GOOGLE_CALENDAR_ID environment variable");

var sqliteConnectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
    ?? "Data Source=fbs.db";

// ── Google Calendar client ─────────────────────────────────────────────────────

var serviceAccountJson = Encoding.UTF8.GetString(Convert.FromBase64String(googleServiceAccountJsonBase64));
var credential = GoogleCredential.FromJson(serviceAccountJson).CreateScoped(
    "https://www.googleapis.com/auth/calendar",
    "https://www.googleapis.com/auth/calendar.events"
);

var calendarService = new CalendarService(new BaseClientService.Initializer
{
    HttpClientInitializer = credential
});

// ── FreeSql (SQLite) ───────────────────────────────────────────────────────────

var freeSql = new FreeSql.FreeSqlBuilder()
    .UseConnectionString(FreeSql.DataType.Sqlite, sqliteConnectionString)
    .UseAutoSyncStructure(true)
    .Build();

freeSql.CodeFirst.ConfigEntity<Booking>(a =>
{
    a.Property(b => b.Row).IsIgnore(true);
});

// ── Read bookings from Google Calendar ─────────────────────────────────────────

Console.WriteLine($"Reading bookings from Google Calendar {googleCalendarId}...");

var bookings = new List<Booking>();
string? pageToken = null;

do
{
    var request = calendarService.Events.List(googleCalendarId);
    if (pageToken is not null)
    {
        request.PageToken = pageToken;
    }

    var items = await request.ExecuteAsync();
    pageToken = items.NextPageToken;

    if (items.Items is null || items.Items.Count == 0)
    {
        break;
    }

    var converted = items.Items
        .Where(item => item.ExtendedProperties?.Shared?.ContainsKey("Data") == true)
        .Select(item =>
        {
            try
            {
                var data = Convert.FromBase64String(item.ExtendedProperties.Shared["Data"]);
                return MemoryPackSerializer.Deserialize<Booking>(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  WARNING: Failed to deserialize event {item.Id}: {ex.Message}");
                return null;
            }
        })
        .Where(b => b is not null)
        .Cast<Booking>()
        .ToList();

    bookings.AddRange(converted);
} while (pageToken is not null);

Console.WriteLine($"Found {bookings.Count} bookings in Google Calendar.");

// ── Insert into SQLite ─────────────────────────────────────────────────────────

var migrated = 0;
var skipped = 0;

foreach (var booking in bookings)
{
    var exists = await freeSql.Select<Booking>()
        .Where(b => b.Id == booking.Id)
        .AnyAsync();

    if (exists)
    {
        Console.WriteLine($"  SKIP: Booking {booking.Id} already exists.");
        skipped++;
        continue;
    }

    await freeSql.Insert(booking).ExecuteAffrowsAsync();
    migrated++;
    Console.WriteLine($"  OK:   Migrated booking {booking.Id}");
}

// ── Summary ────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine($"Migration complete. Total: {bookings.Count}, Migrated: {migrated}, Skipped: {skipped}");

freeSql.Dispose();

// ── Booking entity (mirrors Fbs.WebApi/Entities/Booking.cs) ────────────────────

[MemoryPackable]
[Table(Name = "Bookings")]
public partial class Booking
{
    public int Row { get; set; }

    [Column(IsPrimary = true)]
    [MemoryPackOrder(0)]
    public Guid Id { get; set; }

    [Column(DbType = "TEXT")]
    public DateTimeOffset? StartDateTime { get; set; }

    [Column(DbType = "TEXT")]
    public DateTimeOffset? EndDateTime { get; set; }

    [Column(StringLength = 100)]
    public string? Conduct { get; set; }

    [Column(StringLength = 500)]
    public string? Description { get; set; }

    [Column(StringLength = 100)]
    public string? PocName { get; set; }

    [Column(StringLength = 50)]
    public string? PocPhone { get; set; }

    [Column(StringLength = 100)]
    public string? FacilityName { get; set; }

    [Column(StringLength = 50)]
    public string? UserPhone { get; set; }
}
