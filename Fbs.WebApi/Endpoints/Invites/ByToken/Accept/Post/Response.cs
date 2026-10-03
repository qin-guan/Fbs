using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Invites.ByToken.Accept.Post;

public class Response
{
    public required string Slug { get; init; }

    public required string OrganizationName { get; init; }

    /// <summary><c>Pending</c> until an admin lets them in, or <c>Active</c>.</summary>
    public required MemberStatus Status { get; init; }
}
