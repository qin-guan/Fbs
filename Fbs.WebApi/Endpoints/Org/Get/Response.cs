using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Org.Get;

public class Response
{
    public required string Slug { get; init; }

    public required string Name { get; init; }

    public required string TimeZone { get; init; }

    public required string DefaultCountryCode { get; init; }

    public required int SlotMinutes { get; init; }

    public required Me Me { get; init; }
}

public class Me
{
    public required Guid MemberId { get; init; }

    public required string DisplayName { get; init; }

    public required MemberRole Role { get; init; }

    public required NotificationScope NotificationScope { get; init; }

    public string? Phone { get; init; }
}
