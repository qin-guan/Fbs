namespace Fbs.WebApi.Endpoints.Org.Members.ById.Put;

/// <summary>Whether somebody is in the organisation, which is all that is decided here about whether they can use it.</summary>
public enum MembershipState
{
    /// <summary>In. They are active if they have signed in, and otherwise wait as unclaimed until they do.</summary>
    In = 1,

    Removed = 2,
}

public class Request : MemberBody
{
    public Guid Id { get; set; }

    public MembershipState Membership { get; set; } = MembershipState.In;
}
