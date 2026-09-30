namespace Fbs.WebApi.Endpoints.Tenants.Deletion.Post;

public class Request
{
    public string Slug { get; set; } = string.Empty;

    /// <summary>The address of the organisation again, as it is typed to say that this is what is meant.</summary>
    public string Confirm { get; set; } = string.Empty;
}
