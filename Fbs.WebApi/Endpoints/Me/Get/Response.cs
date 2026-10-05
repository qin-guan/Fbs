using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Me.Get;

public class Response
{
    public required Guid Id { get; init; }

    public string? Name { get; init; }

    public string? Email { get; init; }

    /// <summary>The organisations they belong to, or have asked to.</summary>
    public required List<Membership> Memberships { get; init; }
}

public class Membership
{
    public required string TenantSlug { get; init; }

    public required string TenantName { get; init; }

    public required MemberRole Role { get; init; }

    public required MemberStatus Status { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Whether the organisation can be used: it can't be if it is suspended, or is to be deleted.</summary>
    public required TenantStatus TenantStatus { get; init; }

    /// <summary>When it is deleted for good, if it is to be. Its admins can restore it until then.</summary>
    public DateTimeOffset? DeleteAfter { get; init; }
}
