using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Me.Export.Get;

public class Response
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required AccountData Account { get; init; }

    public required TelegramData Telegram { get; init; }

    /// <summary>Every organisation they are in, or were in, or asked to be.</summary>
    public required List<MembershipData> Memberships { get; init; }

    /// <summary>What they booked, in any of them, cancelled or not.</summary>
    public required List<BookingData> Bookings { get; init; }

    /// <summary>What somebody else booked and they changed or cancelled: only what it was and when, as what it was for is theirs.</summary>
    public required List<ChangeData> OthersBookingsTheyChanged { get; init; }
}

public class AccountData
{
    public required Guid Id { get; init; }

    public string? Name { get; init; }

    public string? Email { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public class TelegramData
{
    public required bool Linked { get; init; }

    public string? ChatId { get; init; }

    public DateTimeOffset? LinkedAt { get; init; }
}

public class MembershipData
{
    public required string OrganizationSlug { get; init; }

    public required string OrganizationName { get; init; }

    public required Guid MemberId { get; init; }

    public required string DisplayName { get; init; }

    public string? Phone { get; init; }

    public string? Unit { get; init; }

    public required MemberRole Role { get; init; }

    public required MemberStatus Status { get; init; }

    public required NotificationScope NotificationScope { get; init; }

    public required DateTimeOffset JoinedAt { get; init; }
}

public class BookingData
{
    public required string OrganizationSlug { get; init; }

    public required Guid Id { get; init; }

    public string? Facility { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required DateTimeOffset End { get; init; }

    public required string Conduct { get; init; }

    public string? Description { get; init; }

    public string? PocName { get; init; }

    public string? PocPhone { get; init; }

    public required DateTimeOffset MadeAt { get; init; }

    public DateTimeOffset? CancelledAt { get; init; }
}

public class ChangeData
{
    public required string OrganizationSlug { get; init; }

    public required Guid Id { get; init; }

    public string? Facility { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required DateTimeOffset End { get; init; }

    /// <summary><c>changed</c> or <c>cancelled</c>.</summary>
    public required string What { get; init; }
}
