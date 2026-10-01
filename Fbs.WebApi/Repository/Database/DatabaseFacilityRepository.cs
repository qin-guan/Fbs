using System.Linq.Expressions;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Entities;
using SqlSugar;
using Facility = Fbs.WebApi.Entities.Facility;

namespace Fbs.WebApi.Repository.Database;

/// <summary>
/// Facilities from the tenant's, with the units that can book them as their scope.
/// </summary>
public class DatabaseFacilityRepository(ISqlSugarClient sql, DefaultTenant tenant) : IFacilityRepository
{
    public async Task<List<Facility>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = await tenant.GetIdAsync(cancellationToken);
        var facilities = await sql.Queryable<Data.Entities.Facility>()
            .Where(f => f.TenantId == tenantId)
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);
        var units = (await sql.Queryable<Data.Entities.Unit>().Where(u => u.TenantId == tenantId).ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => u.Name);
        var access = (await sql.Queryable<FacilityUnitAccess>().Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken))
            .ToLookup(a => a.FacilityId, a => units.GetValueOrDefault(a.UnitId));

        return facilities.Select(f => ToFacility(f, access[f.Id].OfType<string>().Order().ToList())).ToList();
    }

    public async Task<Facility?> FindAsync(Expression<Func<Facility, bool>> predicate, CancellationToken cancellationToken = default) =>
        (await GetListAsync(cancellationToken)).SingleOrDefault(predicate.Compile());

    public async Task<Facility> GetAsync(Expression<Func<Facility, bool>> predicate, CancellationToken cancellationToken = default) =>
        (await GetListAsync(cancellationToken)).Single(predicate.Compile());

    public Task<Facility> InsertAsync(Facility entity, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<Facility> UpdateAsync(Facility entity, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeleteAsync(Expression<Func<Facility, bool>> predicate, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>
    /// A facility that nobody can book has no scope at all, which is different to one whose scope is
    /// empty, and is how the API has always told them apart.
    /// </summary>
    private static Facility ToFacility(Data.Entities.Facility facility, List<string> unitNames) =>
        new()
        {
            Name = facility.Name,
            Group = facility.Group,
            Scope = facility.AvailableToAll ? ["All"] : unitNames.Count > 0 ? unitNames : null,
        };
}
