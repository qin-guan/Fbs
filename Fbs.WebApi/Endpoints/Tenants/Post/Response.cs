namespace Fbs.WebApi.Endpoints.Tenants.Post;

public class Response
{
    public required string Slug { get; init; }

    public required string Name { get; init; }

    public required string TimeZone { get; init; }
}
