using FluentValidation;
using Fbs.WebApi.Data.Entities;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Endpoints.Org.Facilities;

public class FacilityResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Group { get; init; }

    public required bool AvailableToAll { get; init; }

    /// <summary>The units that can book it, when it isn't available to everyone.</summary>
    public required List<Guid> UnitIds { get; init; }

    public static FacilityResponse From(DataFacility facility, IEnumerable<Guid> unitIds) =>
        new()
        {
            Id = facility.Id,
            Name = facility.Name,
            Group = facility.Group,
            AvailableToAll = facility.AvailableToAll,
            UnitIds = unitIds.Order().ToList(),
        };

    /// <summary>
    /// Whether all of the units are the organisation's own. Anything else would be a facility open to a unit in
    /// another organisation, which would then see it.
    /// </summary>
    public static async Task<bool> AllUnitsAreInAsync(ISqlSugarClient sql, Guid tenantId, IReadOnlyCollection<Guid> unitIds, CancellationToken ct)
    {
        if (unitIds.Count == 0)
        {
            return true;
        }

        var ids = unitIds.ToList();
        var known = await sql.Queryable<Data.Entities.Unit>().Where(u => u.TenantId == tenantId && ids.Contains(u.Id)).CountAsync(ct);
        return known == ids.Count;
    }
}

/// <summary>What is sent to make or change a facility.</summary>
public class FacilityBody
{
    public string Name { get; set; } = string.Empty;

    public string? Group { get; set; }

    public bool AvailableToAll { get; set; }

    public List<Guid> UnitIds { get; set; } = [];
}

public class FacilityBodyValidator<T> : FastEndpoints.Validator<T>
    where T : FacilityBody
{
    public FacilityBodyValidator()
    {
        RuleFor(r => r.Name).Must(name => name.Trim().Length is >= 1 and <= 100).WithMessage("The name has to be between 1 and 100 characters.");
        RuleFor(r => r.Group).Must(group => group is null || group.Trim().Length <= 100).WithMessage("The group can be up to 100 characters.");
        RuleFor(r => r.UnitIds).Must((body, ids) => !body.AvailableToAll || ids.Count == 0).WithMessage("A facility that is available to everyone has no units to limit it to.");
    }
}
