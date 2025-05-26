using System.Linq.Expressions;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Repository;

public class NominalRollRepository(
    InstrumentationSource instrumentation,
    HybridCache cache,
    IOptions<GoogleOptions> options,
    SheetsService sheetsService
) : IRepository<NominalRoll>
{
    private readonly string[] _header =
    [
        "Name",
        "Unit",
        "Phone",
    ];

    public async Task<List<NominalRoll>> GetListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await cache.GetOrCreateAsync(
            "Nominal Roll",
            (sheetsService, options),
            async (state, ct) =>
                await state.sheetsService.Spreadsheets.Values.Get(state.options.Value.SpreadsheetId, "Nominal Roll")
                    .ExecuteAsync(ct),
            cancellationToken: cancellationToken);

        if (!items.Values.First().SequenceEqual(_header))
        {
            throw new Exception("Unexpected headers in Nominal Roll table");
        }

        return items.Values
            .Skip(1)
            .Select((row, idx) => new NominalRoll 
            {
                Row = idx + 2,
                Name = row.ElementAtOrDefault(0) as string,
                Unit = row.ElementAtOrDefault(1) as string,
                Phone = row.ElementAtOrDefault(2) as string,
            })
            .ToList();
    }

    public async Task<NominalRoll?> FindAsync(Expression<Func<NominalRoll, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.SingleOrDefault(predicate.Compile());
    }

    public async Task<NominalRoll> GetAsync(Expression<Func<NominalRoll, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.Single(predicate.Compile());
    }

    public async Task<NominalRoll> InsertAsync(NominalRoll entity, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<NominalRoll> UpdateAsync(NominalRoll entity, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task DeleteAsync(Expression<Func<NominalRoll, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}