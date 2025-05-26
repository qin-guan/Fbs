using System.Linq.Expressions;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Repository;

public class UserRepository(
    InstrumentationSource instrumentation,
    IOptions<GoogleOptions> options,
    HybridCache cache,
    SheetsService sheetsService
) : IRepository<User>
{
    private readonly string[] _header =
    [
        "Unit", "Name", "Phone", "TelegramChatId", "NotificationGroup"
    ];

    public async Task<List<User>> GetListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await cache.GetOrCreateAsync(
            "Users",
            (sheetsService, options),
            async (state, ct) =>
                await sheetsService.Spreadsheets.Values.Get(options.Value.SpreadsheetId, "Users")
                    .ExecuteAsync(ct),
            cancellationToken: cancellationToken);

        if (!items.Values.First().SequenceEqual(_header))
        {
            throw new Exception("Unexpected headers for Users sheet");
        }

        return items.Values
            .Skip(1)
            .Select((row, idx) => new User
            {
                Row = idx + 2,
                Unit = row.ElementAtOrDefault(0) as string,
                Name = row.ElementAtOrDefault(1) as string,
                Phone = row.ElementAtOrDefault(2) as string,
                TelegramChatId = row.ElementAtOrDefault(3) as string,
                NotificationGroup = row.ElementAtOrDefault(4) as string,
            })
            .ToList();
    }

    public async Task<User?> FindAsync(Expression<Func<User, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.SingleOrDefault(predicate.Compile());
    }

    public async Task<User> GetAsync(Expression<Func<User, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.Single(predicate.Compile());
    }

    public async Task<User> InsertAsync(User entity, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<User> UpdateAsync(User entity, CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var request = sheetsService.Spreadsheets.Values.Update(
            new ValueRange
            {
                Values = [[entity.Unit, entity.Name, entity.Phone, entity.TelegramChatId, entity.NotificationGroup]]
            },
            options.Value.SpreadsheetId,
            $"Users!A{entity.Row}:E{entity.Row}"
        );

        request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await request.ExecuteAsync(cancellationToken);

        await cache.RemoveAsync("Users", cancellationToken);

        return entity;
    }

    public async Task DeleteAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();
        var entity = await GetAsync(predicate, cancellationToken);

        await sheetsService.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest
        {
            Requests =
            [
                new Request
                {
                    DeleteDimension = new DeleteDimensionRequest
                    {
                        Range = new DimensionRange
                        {
                            SheetId = await GetSheetId(cancellationToken),
                            Dimension = "ROWS",
                            StartIndex = entity.Row - 1,
                            EndIndex = entity.Row
                        }
                    }
                }
            ]
        }, options.Value.SpreadsheetId).ExecuteAsync(cancellationToken);
        
        await cache.RemoveAsync("Users", cancellationToken);
    }

    private async Task<int?> GetSheetId(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var sheet = await sheetsService.Spreadsheets.Get(options.Value.SpreadsheetId).ExecuteAsync(cancellationToken);
        return sheet.Sheets.Single(s => s.Properties.Title == "Users").Properties.SheetId;
    }
}