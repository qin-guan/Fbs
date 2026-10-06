using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Org.Bookings.Get;

public class Request
{
    /// <summary>
    /// The start of a window. Left out together with <see cref="To"/>, every booking is listed. Left out on its own,
    /// the window starts <see cref="Endpoint.DefaultDays"/> days before <see cref="To"/>.
    /// </summary>
    [QueryParam]
    public DateTimeOffset? From { get; set; }

    /// <summary>
    /// The end of a window. Left out together with <see cref="From"/>, every booking is listed. Left out on its own,
    /// the window ends <see cref="Endpoint.DefaultDays"/> days after <see cref="From"/>.
    /// </summary>
    [QueryParam]
    public DateTimeOffset? To { get; set; }

    [QueryParam]
    public Guid? FacilityId { get; set; }

    /// <summary>Only what this member made.</summary>
    [QueryParam]
    public Guid? BookedBy { get; set; }

    /// <summary>Only what the caller made.</summary>
    [QueryParam]
    public bool Mine { get; set; }
}
