using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Audit.Get;

/// <summary>What has been done to the organisation, the latest first, for its admins. People are named as they are now: somebody whose account was erased is a former member.</summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : Endpoint<Request, List<Response>>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public override void Configure()
    {
        Get("/t/{slug}/Audit");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenantId = tenantContext.Tenant.Id;
        var limit = Math.Clamp(req.Limit ?? DefaultLimit, 1, MaxLimit);
        var before = req.Before;

        var entries = await sql.Queryable<AuditEntry>()
            .Where(e => e.TenantId == tenantId)
            .WhereIF(before is not null, e => e.At < before)
            .OrderByDescending(e => e.At)
            .Take(limit)
            .ToListAsync(ct);

        var memberIds = entries.SelectMany(e => new[] { e.ActorMemberId, e.TargetType == "member" ? e.TargetId : null }).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var names = memberIds.Count == 0
            ? []
            : (await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId && memberIds.Contains(m.Id)).Select(m => new { m.Id, m.DisplayName }).ToListAsync(ct)).ToDictionary(m => m.Id, m => m.DisplayName);

        Person? Named(Guid? id) => id is { } known && names.TryGetValue(known, out var name) ? new Person { MemberId = known, DisplayName = name } : null;

        await Send.OkAsync(
            entries
                .Select(e => new Response
                {
                    Id = e.Id,
                    At = e.At,
                    Actor = Named(e.ActorMemberId),
                    Action = e.Action,
                    Summary = e.Summary,
                    TargetType = e.TargetType,
                    TargetId = e.TargetId,
                    Target = e.TargetType == "member" ? Named(e.TargetId) : null,
                })
                .ToList(),
            ct
        );
    }
}
