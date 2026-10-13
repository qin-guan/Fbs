using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Me.Put;

/// <summary>
/// Changes what the caller is called in the organisation, which is what everyone in it sees. Calling themselves by the name on
/// their account again has it follow that name once more (see <see cref="MemberNames"/>).
/// </summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, AuditLog audit) : Endpoint<Request, Get.Me>
{
    public override void Configure()
    {
        Put("/t/{slug}/Me");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var member = tenantContext.Member;
        var id = member.Id;
        var displayName = req.DisplayName.Trim();
        if (displayName != member.DisplayName)
        {
            await sql.Updateable<TenantMember>()
                .SetColumns(m => new TenantMember { DisplayName = displayName })
                .Where(m => m.Id == id && m.TenantId == tenantId)
                .ExecuteCommandAsync(ct);
            await audit.WriteAsync(tenantId, id, "member.renamed", "Changed their name.", "member", id, ct);
        }

        await Send.OkAsync(
            new Get.Me
            {
                MemberId = id,
                DisplayName = displayName,
                Role = member.Role,
                NotificationScope = member.NotificationScope,
                Phone = member.Phone,
            },
            ct
        );
    }
}
