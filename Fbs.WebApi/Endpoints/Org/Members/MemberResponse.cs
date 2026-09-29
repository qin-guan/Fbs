using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Org.Members;

public class MemberResponse
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>In the form it is stored, such as +6591234567.</summary>
    public string? Phone { get; init; }

    public Guid? UnitId { get; init; }

    public required MemberRole Role { get; init; }

    public required NotificationScope NotificationScope { get; init; }

    public required MemberStatus Status { get; init; }

    /// <summary>Whether they have signed in and been matched to an account. Members who haven't are added, or carried over, by phone number.</summary>
    public required bool HasAccount { get; init; }

    public static MemberResponse From(TenantMember member) =>
        new()
        {
            Id = member.Id,
            DisplayName = member.DisplayName,
            Phone = member.Phone,
            UnitId = member.UnitId,
            Role = member.Role,
            NotificationScope = member.NotificationScope,
            Status = member.Status,
            HasAccount = member.UserId is not null,
        };
}
