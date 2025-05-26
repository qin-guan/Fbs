using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Booking.Post;

public class Request
{
    public string? Conduct { get; set; }
    public string? Description { get; set; }
    public string? FacilityName { get; set; }
    public string? PocName { get; set; }
    public string? PocPhone { get; set; }
    public DateTimeOffset StartDateTime { get; set; }
    public DateTimeOffset EndDateTime { get; set; }
}