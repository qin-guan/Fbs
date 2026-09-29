using System.Linq.Expressions;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Entities;
using SqlSugar;
using NominalRoll = Fbs.WebApi.Entities.NominalRoll;

namespace Fbs.WebApi.Repository.Database;

public class DatabaseNominalRollRepository(ISqlSugarClient sql, DefaultTenant tenant) : INominalRollRepository
{
    public async Task<List<NominalRoll>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = await tenant.GetIdAsync(cancellationToken);
        var entries = await sql.Queryable<RosterEntry>()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken);

        return entries
            .Select(e => new NominalRoll { Name = e.Name, Unit = e.Unit, Phone = PhoneNumbers.ToApi(e.Phone) })
            .ToList();
    }

    public async Task<NominalRoll?> FindAsync(Expression<Func<NominalRoll, bool>> predicate, CancellationToken cancellationToken = default) =>
        (await GetListAsync(cancellationToken)).SingleOrDefault(predicate.Compile());

    public async Task<NominalRoll> GetAsync(Expression<Func<NominalRoll, bool>> predicate, CancellationToken cancellationToken = default) =>
        (await GetListAsync(cancellationToken)).Single(predicate.Compile());

    public Task<NominalRoll> InsertAsync(NominalRoll entity, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<NominalRoll> UpdateAsync(NominalRoll entity, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeleteAsync(Expression<Func<NominalRoll, bool>> predicate, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
