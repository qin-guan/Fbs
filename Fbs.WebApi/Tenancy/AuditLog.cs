using Fbs.WebApi.Data.Entities;
using SqlSugar;

namespace Fbs.WebApi.Tenancy;

/// <summary>
/// Writes down what admins do, for the organisation's admins to look at. It is written where the thing is done, in the same
/// transaction if there is one, so that what is shown is what happened. The summary is in words and has no name of a person in it.
/// </summary>
public sealed class AuditLog(ISqlSugarClient sql)
{
    public Task WriteAsync(Guid tenantId, Guid? actorMemberId, string action, string summary, string? targetType, Guid? targetId, CancellationToken ct) =>
        sql.Insertable(
                new AuditEntry
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ActorMemberId = actorMemberId,
                    Action = action,
                    TargetType = targetType,
                    TargetId = targetId,
                    Summary = summary.Length > 500 ? summary[..500] : summary,
                    At = DateTimeOffset.UtcNow,
                }
            )
            .ExecuteCommandAsync(ct);
}
