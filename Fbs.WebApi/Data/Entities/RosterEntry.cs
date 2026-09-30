using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// Someone who can be named as a point of contact, whether or not they use the system.
/// </summary>
[SugarIndex("UX_RosterEntry_TenantId_Phone", nameof(TenantId), OrderByType.Asc, nameof(Phone), OrderByType.Asc, IsUnique = true)]
public class RosterEntry
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    [SugarColumn(Length = 200)]
    public string Name { get; set; } = null!;

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? Unit { get; set; }

    /// <summary>In E.164 form.</summary>
    [SugarColumn(Length = 32)]
    public string Phone { get; set; } = null!;
}
