using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Org.Invites.Post;

public class Request
{
    /// <summary>What whoever joins is. A member if left out.</summary>
    public MemberRole Role { get; set; } = MemberRole.Member;

    /// <summary>The unit they are put in, if any.</summary>
    public Guid? UnitId { get; set; }

    /// <summary>How long it works for, from 1 to 30 days. 7 if left out.</summary>
    public int ExpiresInDays { get; set; } = 7;

    /// <summary>How many people can join with it, from 1 to 100. 10 if left out.</summary>
    public int MaxUses { get; set; } = 10;
}
