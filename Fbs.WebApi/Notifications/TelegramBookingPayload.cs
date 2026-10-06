using System.Text.Json.Serialization;

namespace Fbs.WebApi.Notifications;

public enum BookingChange
{
    Created,
    Updated,
    Cancelled,
}

/// <summary>
/// The outbox message that tells people about a booking change. Bookings made together share one
/// message, so each person is told once for the batch.
/// </summary>
public sealed class TelegramBookingPayload
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BookingChange Change { get; set; }

    public List<Guid> BookingIds { get; set; } = [];

    /// <summary>Who made, changed or cancelled the bookings.</summary>
    public Guid ActorMemberId { get; set; }

    /// <summary>When a booking was moved, where it was, so nobody turns up at the old time.</summary>
    public DateTimeOffset? PreviousStartUtc { get; set; }

    public DateTimeOffset? PreviousEndUtc { get; set; }

    /// <summary>
    /// Who has been told already, kept when sending fails part way, so trying again doesn't tell them twice.
    /// </summary>
    public List<Guid> Delivered { get; set; } = [];
}
