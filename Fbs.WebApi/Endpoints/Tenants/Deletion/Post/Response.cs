namespace Fbs.WebApi.Endpoints.Tenants.Deletion.Post;

public class Response
{
    /// <summary>The earliest it is deleted for good. Until then its admins can restore it.</summary>
    public DateTimeOffset DeleteAfter { get; init; }
}
