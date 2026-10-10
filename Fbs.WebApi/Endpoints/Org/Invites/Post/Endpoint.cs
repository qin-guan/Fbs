using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Endpoints.Org.Members;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Invites.Post;

/// <summary>
/// Makes a link for people to join with. Whoever uses it waits to be let in unless the organisation has turned that
/// off, so a link that is shared further than it should be exposes nothing.
/// </summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, IOptions<TenantLimits> limits, AuditLog audit) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/t/{slug}/Invites");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var now = DateTimeOffset.UtcNow;

        if (!await MemberChecks.UnitIsInAsync(sql, tenantId, req.UnitId, ct))
        {
            AddError(r => r.UnitId!, "That isn't one of this organisation's units.", "unit-unknown");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var active = await sql.Queryable<TenantInvite>().CountAsync(i => i.TenantId == tenantId && i.RevokedAt == null && i.ExpiresAt > now && i.Uses < i.MaxUses, ct);
        if (active >= limits.Value.MaxActiveInvites)
        {
            AddError($"There can be {limits.Value.MaxActiveInvites} invite links at a time. Revoke one that isn't needed.", "invite-limit");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var token = InviteTokens.Generate();
        var invite = new TenantInvite
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TokenHash = InviteTokens.Hash(token),
            Role = req.Role,
            UnitId = req.UnitId,
            ExpiresAt = now.AddDays(req.ExpiresInDays),
            MaxUses = req.MaxUses,
            CreatedByMemberId = tenantContext.Member.Id,
            CreatedAt = now,
        };
        await sql.Insertable(invite).ExecuteCommandAsync(ct);
        await audit.WriteAsync(
            tenantId,
            tenantContext.Member.Id,
            "invite.created",
            $"Made an invite link to join as {(req.Role == MemberRole.Admin ? "an admin" : "a member")}, for {req.ExpiresInDays} {(req.ExpiresInDays == 1 ? "day" : "days")} and up to {req.MaxUses} {(req.MaxUses == 1 ? "person" : "people")}.",
            "invite",
            invite.Id,
            ct
        );

        await Send.ResponseAsync(
            new Response
            {
                Id = invite.Id,
                Role = invite.Role,
                UnitId = invite.UnitId,
                ExpiresAt = invite.ExpiresAt,
                MaxUses = invite.MaxUses,
                Uses = 0,
                Status = InviteStatus.Active,
                CreatedAt = invite.CreatedAt,
                Token = token,
            },
            StatusCodes.Status201Created,
            ct
        );
    }
}
