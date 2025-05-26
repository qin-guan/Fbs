using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Booking.ById.Delete;

public class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}