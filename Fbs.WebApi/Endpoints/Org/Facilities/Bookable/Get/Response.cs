namespace Fbs.WebApi.Endpoints.Org.Facilities.Bookable.Get;

public class Response
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Group { get; init; }
}
