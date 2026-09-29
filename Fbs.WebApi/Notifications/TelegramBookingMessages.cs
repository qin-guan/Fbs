using System.Globalization;
using System.Net;
using System.Text;

namespace Fbs.WebApi.Notifications;

/// <summary>A booking as it is told, with its times in the tenant's time zone.</summary>
public sealed record BookingSummary(Guid Id, string Facility, DateTimeOffset Start, DateTimeOffset End);

public sealed record Person(string? Unit, string Name, string? Phone);

/// <summary>
/// The text of the Telegram message about a booking change, which is sent as HTML.
/// </summary>
public static class TelegramBookingMessages
{
    /// <summary>Telegram allows 4096 characters. The rest is left for what is added to a message that is cut short.</summary>
    public const int MaxLength = 4000;

    /// <param name="bookings">One for a single booking, or those made together.</param>
    /// <param name="actor">Who made, changed or cancelled them.</param>
    /// <param name="batchId">What the bookings made together have in common, to refer to them by.</param>
    public static string Build(
        BookingChange change,
        IReadOnlyList<BookingSummary> bookings,
        string? conduct,
        string? description,
        string? pocName,
        string? pocPhone,
        Person actor,
        DateTimeOffset? previousStart = null,
        DateTimeOffset? previousEnd = null,
        Guid? batchId = null
    )
    {
        var verb = change switch
        {
            BookingChange.Created => "CREATED",
            BookingChange.Updated => "UPDATED",
            _ => "CANCELLED",
        };
        var by = change switch
        {
            BookingChange.Created => "Booked by",
            BookingChange.Updated => "Updated by",
            _ => "Cancelled by",
        };

        var footer = new StringBuilder();
        footer.Append($"\nPOC: {Encode(pocName)}, {Encode(pocPhone)}");
        footer.Append($"\n{by}: {Encode(actor.Unit)} / {Encode(actor.Name)}, {Encode(actor.Phone)}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            footer.Append($"\n{Encode(description)}");
        }

        if (bookings.Count == 1)
        {
            var booking = bookings[0];
            var previously =
                previousStart is null || previousEnd is null
                    ? string.Empty
                    : $"\nWas: {Format(previousStart.Value)} – {Format(previousEnd.Value)}";

            return $"<b>{verb}</b> · <b>{Encode(booking.Facility)}</b>\n{Encode(conduct)}\n{Format(booking.Start)} – {Format(booking.End)}{previously}{footer}\nRef: {booking.Id}";
        }

        var header = $"<b>{verb}</b> · <b>{bookings.Count} bookings</b>\n{Encode(conduct)}";
        var reference = batchId is { } batch ? $"\nRef: {batch}" : string.Empty;

        var budget = MaxLength - header.Length - footer.Length - reference.Length - 40;
        var lines = new StringBuilder();
        var listed = 0;
        foreach (var booking in bookings)
        {
            var line = $"\n• <b>{Encode(booking.Facility)}</b> · {Format(booking.Start)} – {Format(booking.End)} ({ShortRef(booking.Id)})";
            if (lines.Length + line.Length > budget)
            {
                break;
            }

            lines.Append(line);
            listed++;
        }

        if (listed < bookings.Count)
        {
            lines.Append($"\n…and {bookings.Count - listed} more");
        }

        return header + lines + footer + reference;
    }

    /// <summary>Spelled out, the same wherever the server is, and in the time zone it is given.</summary>
    private static string Format(DateTimeOffset time) => time.ToString("f", CultureInfo.InvariantCulture);

    private static string ShortRef(Guid id) => id.ToString("N")[..8];

    private static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
