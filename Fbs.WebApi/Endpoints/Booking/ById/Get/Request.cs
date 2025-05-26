using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Booking.ById.Get;

public class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}