using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Org.Bookings.Get;

public class Request
{
    /// <summary>The start of the window. The start of today, in the organisation's time zone, if left out.</summary>
    [QueryParam]
    public DateTimeOffset? From { get; set; }

    /// <summary>The end of the window. <see cref="Endpoint.DefaultDays"/> days after <see cref="From"/> if left out.</summary>
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
