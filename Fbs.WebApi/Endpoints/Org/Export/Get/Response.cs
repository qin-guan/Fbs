using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Org.Export.Get;

public class Response
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required OrganizationData Organization { get; init; }

    public required List<UnitData> Units { get; init; }

    public required List<FacilityData> Facilities { get; init; }

    public required List<MemberData> Members { get; init; }

    /// <summary>Every booking there has been, cancelled ones included.</summary>
    public required List<BookingData> Bookings { get; init; }

    /// <summary>What has been made, without the links themselves: only a hash of each is kept, and it can't be used to join.</summary>
    public required List<InviteData> Invites { get; init; }

    public required List<AuditData> History { get; init; }
}

public class OrganizationData
{
    public required string Slug { get; init; }

    public required string Name { get; init; }

    public required string TimeZone { get; init; }

    public required string DefaultCountryCode { get; init; }

    public required int SlotMinutes { get; init; }

    public required bool RequireApproval { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public class UnitData
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }
}

public class FacilityData
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Group { get; init; }

    public required bool AvailableToAll { get; init; }

    public required List<Guid> UnitIds { get; init; }
}

public class MemberData
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public string? Phone { get; init; }

    public Guid? UnitId { get; init; }

    public required MemberRole Role { get; init; }

    public required MemberStatus Status { get; init; }

    public required NotificationScope NotificationScope { get; init; }

    /// <summary>Whether they have signed in, and so have an account.</summary>
    public required bool HasAccount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public class BookingData
{
    public required Guid Id { get; init; }

    public required Guid FacilityId { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required DateTimeOffset End { get; init; }

    public required string Conduct { get; init; }

    public string? Description { get; init; }

    public string? PocName { get; init; }

    public string? PocPhone { get; init; }

    public required Guid BookedByMemberId { get; init; }

    public Guid? UpdatedByMemberId { get; init; }

    public Guid? UnitId { get; init; }

    public Guid? BatchId { get; init; }

    public required int Revision { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? CancelledAt { get; init; }

    public Guid? CancelledByMemberId { get; init; }
}

public class InviteData
{
    public required Guid Id { get; init; }

    public required MemberRole Role { get; init; }

    public Guid? UnitId { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required int MaxUses { get; init; }

    public required int Uses { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public class AuditData
{
    public required DateTimeOffset At { get; init; }

    public Guid? ActorMemberId { get; init; }

    public required string Action { get; init; }

    public required string Summary { get; init; }

    public string? TargetType { get; init; }

    public Guid? TargetId { get; init; }
}
