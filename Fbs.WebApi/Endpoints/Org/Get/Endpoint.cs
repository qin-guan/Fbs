using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;

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

/// <summary>The organisation, and who the caller is in it.</summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("");
        Group<TenantGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        var member = tenantContext.Member;
        await Send.OkAsync(
            new Response
            {
                Slug = tenant.Slug,
                Name = tenant.Name,
                TimeZone = tenant.TimeZone,
                DefaultCountryCode = tenant.DefaultCountryCode,
                SlotMinutes = tenant.SlotMinutes,
                Me = new Me
                {
                    MemberId = member.Id,
                    DisplayName = member.DisplayName,
                    Role = member.Role,
                    NotificationScope = member.NotificationScope,
                    Phone = member.Phone,
                },
            },
            ct
        );
    }
}
