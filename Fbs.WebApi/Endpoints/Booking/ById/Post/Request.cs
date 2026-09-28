using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Booking.ById.Post;

public class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    public string? Conduct { get; set; }
    public string? Description { get; set; }
    public string? PocName { get; set; }
    public string? PocPhone { get; set; }

    /// <summary>
    /// New start of the booking. Leave both times out to keep the current time slot.
    /// </summary>
    public DateTimeOffset? StartDateTime { get; set; }

    /// <summary>
    /// New end of the booking. Leave both times out to keep the current time slot.
    /// </summary>
    public DateTimeOffset? EndDateTime { get; set; }
}
