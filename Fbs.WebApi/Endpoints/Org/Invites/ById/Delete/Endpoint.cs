using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Invites.ById.Delete;

/// <summary>Stops a link working. Those who joined with it stay.</summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, AuditLog audit) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/t/{slug}/Invites/{id}");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var id = req.Id;
        var now = DateTimeOffset.UtcNow;
        if (!await sql.Queryable<TenantInvite>().AnyAsync(i => i.Id == id && i.TenantId == tenantId, ct))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // One that is already revoked keeps the time it was
        var stopped = await sql.Updateable<TenantInvite>().SetColumns(i => new TenantInvite { RevokedAt = now }).Where(i => i.Id == id && i.TenantId == tenantId && i.RevokedAt == null).ExecuteCommandAsync(ct);
        if (stopped > 0)
        {
            await audit.WriteAsync(tenantId, tenantContext.Member.Id, "invite.stopped", "Stopped an invite link.", "invite", id, ct);
        }

        await Send.NoContentAsync(ct);
    }
}
