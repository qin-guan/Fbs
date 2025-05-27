using System.Linq.Expressions;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Repository;

public class FacilityRepository(
    InstrumentationSource instrumentation,
    HybridCache cache,
    IOptions<GoogleOptions> options,
    SheetsService sheetsService
) : IRepository<Facility>
{
    private readonly string[] _header =
    [
        "Name",
        "Group",
        "Scope"
    ];

    public async Task<List<Facility>> GetListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await cache.GetOrCreateAsync(
            "Facilities",
            (sheetsService, options),
            async (state, ct) =>
                await state.sheetsService.Spreadsheets.Values.Get(state.options.Value.SpreadsheetId, "Facilities")
                    .ExecuteAsync(ct),
            cancellationToken: cancellationToken);

        if (!items.Values.First().SequenceEqual(_header))
        {
            throw new Exception("Unexpected headers in Facilities table");
        }

        return items.Values
            .Skip(1)
            .Select((row, idx) => new Facility
            {
                Row = idx + 2,
                Name = row.ElementAtOrDefault(0) as string,
                Group = row.ElementAtOrDefault(1) as string,
                Scope = (row.ElementAtOrDefault(2) as string)?
                    .Split(",")
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList(),
            })
            .ToList();
    }

    public async Task<Facility?> FindAsync(Expression<Func<Facility, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.SingleOrDefault(predicate.Compile());
    }

    public async Task<Facility> GetAsync(Expression<Func<Facility, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.Single(predicate.Compile());
    }

    public async Task<Facility> InsertAsync(Facility entity, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<Facility> UpdateAsync(Facility entity, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task DeleteAsync(Expression<Func<Facility, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}