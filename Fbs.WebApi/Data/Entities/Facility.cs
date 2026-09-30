using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

[SugarIndex("UX_Facility_TenantId_Name", nameof(TenantId), OrderByType.Asc, nameof(Name), OrderByType.Asc, IsUnique = true)]
public class Facility
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    [SugarColumn(Length = 100)]
    public string Name { get; set; } = null!;

    /// <summary>What kind of facility it is, which facilities are grouped by when choosing one.</summary>
    [SugarColumn(Length = 100, IsNullable = true)]
    public string? Group { get; set; }

    /// <summary>Whether every unit can book it, rather than only the ones in <see cref="FacilityUnitAccess"/>.</summary>
    public bool AvailableToAll { get; set; }
}
