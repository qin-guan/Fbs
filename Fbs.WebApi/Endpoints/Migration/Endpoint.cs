using FastEndpoints;
using Fbs.WebApi.Options;
using Google.Apis.Calendar.v3;
using MemoryPack;
using Microsoft.Extensions.Options;
using BookingEntity = Fbs.WebApi.Entities.Booking;

namespace Fbs.WebApi.Endpoints.Migration;

public class Endpoint(
    IFreeSql freeSql,
    IOptions<GoogleOptions> options,
    CalendarService calendarService,
    ILogger<Endpoint> logger
) : EndpointWithoutRequest<MigrateResponse>
{
    public override void Configure()
    {
        Get("/Migration/FromGoogleCalendar");
        Tags("Migration");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var bookings = new List<BookingEntity>();
        string? token = null;

        do
        {
            var request = calendarService.Events.List(options.Value.CalendarId);
            if (token is not null)
            {
                request.PageToken = token;
            }

            var items = await request.ExecuteAsync(ct);
            token = items.NextPageToken;

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
                        return MemoryPackSerializer.Deserialize<BookingEntity>(data);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to deserialize booking from event {EventId}", item.Id);
                        return null;
                    }
                })
                .Where(b => b is not null)
                .Cast<BookingEntity>()
                .ToList();

            bookings.AddRange(converted);
        } while (token is not null);

        logger.LogInformation("Found {Count} bookings in Google Calendar to migrate", bookings.Count);

        var migrated = 0;
        var skipped = 0;

        foreach (var booking in bookings)
        {
            var exists = await freeSql.Select<BookingEntity>()
                .Where(b => b.Id == booking.Id)
                .AnyAsync(ct);

            if (exists)
            {
                logger.LogInformation("Skipping booking {BookingId} - already exists in database", booking.Id);
                skipped++;
                continue;
            }

            await freeSql.Insert(booking).ExecuteAffrowsAsync(ct);
            migrated++;
            logger.LogInformation("Migrated booking {BookingId}", booking.Id);
        }

        await Send.OkAsync(new MigrateResponse
        {
            TotalFound = bookings.Count,
            Migrated = migrated,
            Skipped = skipped,
        }, ct);
    }
}

public class MigrateResponse
{
    public int TotalFound { get; set; }
    public int Migrated { get; set; }
    public int Skipped { get; set; }
}
