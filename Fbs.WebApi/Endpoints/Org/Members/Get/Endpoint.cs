using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Members.Get;

/// <summary>Everyone in the organisation, for its admins to manage, including those waiting to be let in.</summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : Endpoint<Request, List<MemberResponse>>
{
    public override void Configure()
    {
        Get("/t/{slug}/Members");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var removed = MemberStatus.Removed;
        var includeRemoved = req.IncludeRemoved;
        var members = await sql.Queryable<TenantMember>()
            .Where(m => m.TenantId == tenantId && (includeRemoved || m.Status != removed))
            .OrderBy(m => m.DisplayName)
            .ToListAsync(ct);

        await Send.OkAsync(members.Select(MemberResponse.From).ToList(), ct);
    }
}
