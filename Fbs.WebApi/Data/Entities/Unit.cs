using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// A part of a tenant that facilities can be limited to, and whose members can manage each
/// other's bookings.
/// </summary>
[SugarIndex("UX_Unit_TenantId_Name", nameof(TenantId), OrderByType.Asc, nameof(Name), OrderByType.Asc, IsUnique = true)]
public class Unit
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    [SugarColumn(Length = 100)]
    public string Name { get; set; } = null!;
}
