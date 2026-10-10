using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data.Entities;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Me.Get;

/// <summary>Who is signed in, and which organisations they are in: what the app needs to decide where to take them.</summary>
[RequiresAccounts]
public class Endpoint(ICurrentAccount currentAccount, ISqlSugarClient sql) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/Me");
        AuthSchemes(AccountAuthentication.Scheme);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var account = await currentAccount.GetAsync(ct);
        if (account is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var accountId = account.Id;
        var removed = MemberStatus.Removed;
        var members = await sql.Queryable<TenantMember>().Where(m => m.UserId == accountId && m.Status != removed).ToListAsync(ct);
        var tenantIds = members.Select(m => m.TenantId).Distinct().ToList();
        var tenants = tenantIds.Count == 0
            ? new Dictionary<Guid, Tenant>()
            : (await sql.Queryable<Tenant>().Where(t => tenantIds.Contains(t.Id)).ToListAsync(ct)).ToDictionary(t => t.Id);

        await Send.OkAsync(
            new Response
            {
                Id = account.Id,
                Name = account.Name,
                Email = account.Email,
                Memberships = members
                    .Where(m => tenants.ContainsKey(m.TenantId))
                    .Select(m => new Membership
                    {
                        TenantSlug = tenants[m.TenantId].Slug,
                        TenantName = tenants[m.TenantId].Name,
                        Role = m.Role,
                        Status = m.Status,
                        DisplayName = m.DisplayName,
                        TenantStatus = tenants[m.TenantId].Status,
                        DeleteAfter = tenants[m.TenantId].Status == TenantStatus.PendingDeletion ? tenants[m.TenantId].DeleteAfter : null,
                    })
                    .OrderBy(m => m.TenantName)
                    .ToList(),
            },
            ct
        );
    }
}
