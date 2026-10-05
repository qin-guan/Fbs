using FastEndpoints;
using Fbs.WebApi.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Endpoints.Org.Invites;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Invites.ByToken.Accept.Post;

/// <summary>
/// Joins with a link. Somebody who is in already, or waiting, is told how things stand and nothing is used; somebody an
/// admin has removed can't join again with a link, only be let back in by an admin.
/// </summary>
[RequiresClerk]
public class Endpoint(ICurrentAccount currentAccount, ISqlSugarClient sql, TenantQuotas quotas, AuditLog audit) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/Invites/{token}/Accept");
        AuthSchemes(ClerkAuthentication.Scheme);
        Options(x => x.RequireRateLimiting(RateLimitPolicies.Join));
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var account = await currentAccount.GetAsync(ct);
        if (account is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var hash = InviteTokens.Hash(req.Token);
        var invite = await sql.Queryable<TenantInvite>().FirstAsync(i => i.TokenHash == hash, ct);
        var tenantId = invite?.TenantId;
        var tenant = invite is null ? null : await sql.Queryable<Tenant>().FirstAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, ct);
        if (invite is null || tenant is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var accountId = account.Id;
        var existing = await sql.Queryable<TenantMember>().FirstAsync(m => m.TenantId == tenantId && m.UserId == accountId, ct);
        if (existing is not null)
        {
            await Respond(tenant, existing, ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (InviteResponse.StatusOf(invite, now) != InviteStatus.Active)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (await quotas.CheckMemberAsync(invite.TenantId, ct) is { } refusal)
        {
            AddError(refusal.Reason, refusal.Code);
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var member = new TenantMember
        {
            Id = Guid.NewGuid(),
            TenantId = invite.TenantId,
            UserId = accountId,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? account.Name ?? account.Email ?? "Member" : req.DisplayName.Trim(),
            UnitId = invite.UnitId,
            Role = invite.Role,
            Status = tenant.RequireApproval ? MemberStatus.Pending : MemberStatus.Active,
        };

        var inviteId = invite.Id;
        var joined = await TransactionRetry.RunAsync(
            async () =>
            {
                try
                {
                    using var tran = sql.Ado.UseTran();
                    // A use is taken in the same statement that checks there is one left, so however many are joining
                    // at the same moment, no more than the link allows get in
                    var taken = await sql.Updateable<TenantInvite>()
                        .SetColumns(i => new TenantInvite { Uses = i.Uses + 1 })
                        .Where(i => i.Id == inviteId && i.RevokedAt == null && i.ExpiresAt > now && i.Uses < i.MaxUses)
                        .ExecuteCommandAsync(ct);
                    if (taken == 0)
                    {
                        return Joined.NoUsesLeft;
                    }

                    await sql.Insertable(member).ExecuteCommandAsync(ct);
                    await audit.WriteAsync(
                        member.TenantId,
                        member.Id,
                        "member.joined",
                        member.Status == MemberStatus.Pending ? "Joined with an invite link, and is waiting to be let in." : "Joined with an invite link.",
                        "member",
                        member.Id,
                        ct
                    );
                    tran.CommitTran();
                    return Joined.Yes;
                }
                catch (Exception e) when (e.IsDuplicate())
                {
                    // The same person joining twice at the same moment: the other went first, and used the only use
                    return Joined.AlreadyMember;
                }
            },
            ct
        );

        switch (joined)
        {
            case Joined.NoUsesLeft:
                await Send.NotFoundAsync(ct);
                return;
            case Joined.AlreadyMember:
                var other = await sql.Queryable<TenantMember>().FirstAsync(m => m.TenantId == tenantId && m.UserId == accountId, ct);
                await Respond(tenant, other ?? member, ct);
                return;
        }

        await Respond(tenant, member, ct);
    }

    private enum Joined
    {
        Yes,
        NoUsesLeft,
        AlreadyMember,
    }

    private async Task Respond(Tenant tenant, TenantMember member, CancellationToken ct)
    {
        if (member.Status == MemberStatus.Removed)
        {
            AddError("An admin removed you from this organization, so only an admin can let you back in.", "removed");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        await Send.OkAsync(new Response { Slug = tenant.Slug, OrganizationName = tenant.Name, Status = member.Status }, ct);
    }
}
