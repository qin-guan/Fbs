namespace Fbs.WebApi.Endpoints.Org.Invites.Post;

public class Response : InviteResponse
{
    /// <summary>What to put in the link. It is only known now: only a hash of it is kept.</summary>
    public required string Token { get; init; }
}
