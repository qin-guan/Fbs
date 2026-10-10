namespace Fbs.WebApi.Endpoints.Invites.ByToken.Accept.Post;

public class Request
{
    public string Token { get; set; } = string.Empty;

    /// <summary>What they are called in the organisation. Their name on their account if left out.</summary>
    public string? DisplayName { get; set; }
}
