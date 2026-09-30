namespace Fbs.WebApi.Endpoints.Invites.ByToken.Get;

public class Response
{
    public required string OrganizationName { get; init; }

    /// <summary>Whether they will wait for an admin to let them in.</summary>
    public required bool RequiresApproval { get; init; }
}
