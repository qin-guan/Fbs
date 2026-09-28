using System.Net;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using MemoryPack;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Repository;

/// <summary>
/// Keeps every booking in memory, in step with the storage calendar.
/// </summary>
/// <remarks>
/// The calendar is only downloaded in full once. After that, only the events changed since the
/// last sync are fetched (see https://developers.google.com/workspace/calendar/api/guides/sync),
/// and changes made through the API are applied as they happen, so reading bookings stays fast
/// however many there are.
/// </remarks>
public sealed class BookingCache(
    InstrumentationSource instrumentation,
    ILogger<BookingCache> logger,
    IOptions<GoogleOptions> options,
    CalendarService calendarService
)
{
    /// <summary>
    /// The most events the Calendar API returns in one page.
    /// </summary>
    private const int PageSize = 2500;

    /// <summary>
    /// Only what's needed to read bookings, which keeps pages small.
    /// </summary>
    private const string Fields =
        "nextPageToken,nextSyncToken,items(id,status,start,end,extendedProperties/shared)";

    /// <summary>
    /// Serialises changes to the bookings and sync token. Reading bookings doesn't need it.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Bookings by event ID, or null until they are loaded. A published dictionary is never
    /// modified, changes swap in a new one, so readers can use it without locking.
    /// </summary>
    private volatile Dictionary<string, Booking>? _bookings;

    private string? _syncToken;

    /// <summary>
    /// The bookings as of the last sync, loading them first if they haven't been.
    /// </summary>
    /// <remarks>The bookings are shared, so they must not be modified.</remarks>
    public async Task<IReadOnlyCollection<Booking>> GetAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_bookings is { } bookings)
        {
            return bookings.Values;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_bookings is null)
            {
                await LoadAllAsync(cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }

        return _bookings!.Values;
    }

    /// <summary>
    /// Fetches changes made to the calendar since the last sync, then returns the bookings.
    /// </summary>
    /// <remarks>The bookings are shared, so they must not be modified.</remarks>
    public async Task<IReadOnlyCollection<Booking>> GetLatestAsync(
        CancellationToken cancellationToken = default
    )
    {
        await SyncAsync(cancellationToken);
        return _bookings!.Values;
    }

    /// <summary>
    /// Fetches changes made to the calendar since the last sync, or every booking if there
    /// hasn't been one.
    /// </summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_bookings is null || _syncToken is null)
            {
                await LoadAllAsync(cancellationToken);
                return;
            }

            try
            {
                await LoadChangesAsync(_bookings, _syncToken, cancellationToken);
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.Gone)
            {
                logger.LogInformation("Calendar sync token expired, reloading every booking");
                await LoadAllAsync(cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Downloads every booking again. The current bookings are served until it finishes.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await LoadAllAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Adds or replaces a booking once its event has been saved to the calendar.
    /// </summary>
    public Task SetAsync(Booking booking)
    {
        return ChangeAsync(bookings => bookings[EventId(booking.Id)] = booking);
    }

    /// <summary>
    /// Removes a booking once its event has been deleted from the calendar.
    /// </summary>
    public Task RemoveAsync(Guid id)
    {
        return ChangeAsync(bookings => bookings.Remove(EventId(id)));
    }

    private async Task ChangeAsync(Action<Dictionary<string, Booking>> change)
    {
        // Not cancellable, as the calendar has already been changed
        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            // If bookings haven't been loaded yet, loading them will pick up the change
            if (_bookings is not null)
            {
                var bookings = new Dictionary<string, Booking>(_bookings);
                change(bookings);
                _bookings = bookings;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var (changes, syncToken) = await ListEventsAsync(null, cancellationToken);

        var bookings = new Dictionary<string, Booking>(changes.Count);
        foreach (var (id, booking) in changes)
        {
            if (booking is not null)
            {
                bookings[id] = booking;
            }
        }

        _bookings = bookings;
        _syncToken = syncToken;

        activity?.SetTag("fbs.bookings.count", bookings.Count);
        logger.LogInformation("Loaded {Count} bookings from the calendar", bookings.Count);
    }

    private async Task LoadChangesAsync(
        Dictionary<string, Booking> current,
        string syncToken,
        CancellationToken cancellationToken
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var (changes, nextSyncToken) = await ListEventsAsync(syncToken, cancellationToken);

        if (changes.Count > 0)
        {
            var bookings = new Dictionary<string, Booking>(current);
            foreach (var (id, booking) in changes)
            {
                if (booking is null)
                {
                    bookings.Remove(id);
                }
                else
                {
                    bookings[id] = booking;
                }
            }

            _bookings = bookings;
            logger.LogDebug("Applied {Count} changes from the calendar", changes.Count);
        }

        _syncToken = nextSyncToken;

        activity?.SetTag("fbs.bookings.changes", changes.Count);
    }

    /// <summary>
    /// Lists every event in the calendar, or only those changed since the sync token.
    /// </summary>
    /// <returns>
    /// The booking held by each event, or null if it was deleted or holds no booking, along with
    /// the token for the next sync.
    /// </returns>
    private async Task<(
        List<(string Id, Booking? Booking)> Changes,
        string? SyncToken
    )> ListEventsAsync(string? syncToken, CancellationToken cancellationToken)
    {
        var changes = new List<(string, Booking?)>();
        string? pageToken = null;

        while (true)
        {
            var request = calendarService.Events.List(options.Value.CalendarId);
            request.MaxResults = PageSize;
            request.Fields = Fields;
            request.SyncToken = syncToken;
            request.PageToken = pageToken;

            var page = await request.ExecuteAsync(cancellationToken);
            foreach (var @event in page.Items ?? [])
            {
                changes.Add((@event.Id, ToBooking(@event)));
            }

            if (page.NextPageToken is null)
            {
                return (changes, page.NextSyncToken);
            }

            pageToken = page.NextPageToken;
        }
    }

    private static Booking? ToBooking(Event @event)
    {
        var shared = @event.ExtendedProperties?.Shared;
        if (
            @event.Status == "cancelled"
            || shared is null
            || !shared.TryGetValue("Data", out var data)
        )
        {
            return null;
        }

        var booking = MemoryPackSerializer.Deserialize<Booking>(Convert.FromBase64String(data));
        if (booking is null)
        {
            return null;
        }

        var eventStartDateTime = @event.Start?.DateTimeDateTimeOffset;
        var eventEndDateTime = @event.End?.DateTimeDateTimeOffset;
        if (eventStartDateTime is null || eventEndDateTime is null)
        {
            return null;
        }

        booking.StartDateTime = eventStartDateTime;
        booking.EndDateTime = eventEndDateTime;
        return booking;
    }

    private static string EventId(Guid id) => id.ToString("N");
}
