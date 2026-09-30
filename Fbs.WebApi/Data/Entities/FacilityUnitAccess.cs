using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// A unit that can book a facility that isn't available to everyone.
/// </summary>
[SugarIndex("UX_FacilityUnitAccess_FacilityId_UnitId", nameof(FacilityId), OrderByType.Asc, nameof(UnitId), OrderByType.Asc, IsUnique = true)]
public class FacilityUnitAccess
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid FacilityId { get; set; }

    public Guid UnitId { get; set; }
}
