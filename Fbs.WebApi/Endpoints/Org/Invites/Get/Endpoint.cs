using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Invites.Get;

/// <summary>The invite links the organisation has made, the latest 100, and whether each still works. The links themselves aren't kept, only shown when made.</summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : EndpointWithoutRequest<List<InviteResponse>>
{
    public override void Configure()
    {
        Get("/t/{slug}/Invites");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var invites = await sql.Queryable<TenantInvite>().Where(i => i.TenantId == tenantId).OrderByDescending(i => i.CreatedAt).Take(100).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        await Send.OkAsync(invites.Select(i => InviteResponse.From(i, now)).ToList(), ct);
    }
}
