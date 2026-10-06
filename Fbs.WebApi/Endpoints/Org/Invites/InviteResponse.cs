using System.Text.Json.Serialization;
using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Org.Invites;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InviteStatus
{
    Active = 1,
    Expired = 2,
    Revoked = 3,
    UsedUp = 4,
}

public class InviteResponse
{
    public required Guid Id { get; init; }

    public required MemberRole Role { get; init; }

    public Guid? UnitId { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required int MaxUses { get; init; }

    public required int Uses { get; init; }

    public required InviteStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public static InviteResponse From(TenantInvite invite, DateTimeOffset now) =>
        new()
        {
            Id = invite.Id,
            Role = invite.Role,
            UnitId = invite.UnitId,
            ExpiresAt = invite.ExpiresAt,
            MaxUses = invite.MaxUses,
            Uses = invite.Uses,
            Status = StatusOf(invite, now),
            CreatedAt = invite.CreatedAt,
        };

    public static InviteStatus StatusOf(TenantInvite invite, DateTimeOffset now) =>
        invite.RevokedAt is not null ? InviteStatus.Revoked
        : invite.ExpiresAt <= now ? InviteStatus.Expired
        : invite.Uses >= invite.MaxUses ? InviteStatus.UsedUp
        : InviteStatus.Active;
}
