using FastEndpoints;
using Fbs.WebApi.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Invites.ByToken.Get;

/// <summary>
/// What a link is for, so somebody can see where they are joining before they do. A link that doesn't work, for
/// whatever reason, is not found, so they can't be told apart.
/// </summary>
[RequiresAccounts]
public class Endpoint(ICurrentAccount currentAccount, ISqlSugarClient sql) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/Invites/{token}");
        AuthSchemes(AccountAuthentication.Scheme);
        Options(x => x.RequireRateLimiting(RateLimitPolicies.Join));
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        if (await currentAccount.GetAsync(ct) is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var hash = InviteTokens.Hash(req.Token);
        var now = DateTimeOffset.UtcNow;
        var invite = await sql.Queryable<TenantInvite>().FirstAsync(i => i.TokenHash == hash && i.RevokedAt == null && i.ExpiresAt > now && i.Uses < i.MaxUses, ct);
        var tenantId = invite?.TenantId;
        var tenant = invite is null ? null : await sql.Queryable<Tenant>().FirstAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, ct);
        if (tenant is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new Response { OrganizationName = tenant.Name, RequiresApproval = tenant.RequireApproval }, ct);
    }
}
