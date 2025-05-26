using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Facility.TimeSlots.Get;

public class Request
{
    [QueryParam]
    public DateTimeOffset StartTime { get; set; }
    [QueryParam]
    public DateTimeOffset EndTime { get; set; }
    [RouteParam]
    public string Name { get; set; }
}