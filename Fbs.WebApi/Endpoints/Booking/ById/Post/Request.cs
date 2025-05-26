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
}