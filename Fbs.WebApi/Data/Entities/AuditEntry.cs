using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// Something an admin, or the system, did to an organisation: who did what, and when. It keeps no name of a person: who did it
/// and who it was done to are kept as who they are in the organisation, and are turned into names when it is read, so that
/// when somebody's account is erased the names go from here too.
/// </summary>
[SugarIndex("IX_AuditEntry_TenantId_At", nameof(TenantId), OrderByType.Asc, nameof(At), OrderByType.Desc)]
public class AuditEntry
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Who did it. Nobody, if it was done by whoever runs the system.</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? ActorMemberId { get; set; }

    /// <summary>What was done, such as <c>facility.created</c>: the thing, and what happened to it.</summary>
    [SugarColumn(Length = 64)]
    public string Action { get; set; } = null!;

    /// <summary>What it was done to, if it was one thing, such as <c>member</c>.</summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? TargetType { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? TargetId { get; set; }

    /// <summary>What was done, in words, without the name of a person: those are worked out from who did it and to whom.</summary>
    [SugarColumn(Length = 500)]
    public string Summary { get; set; } = null!;

    [SugarColumn(ColumnDataType = "datetime(6)")]
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
