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
        "Unit",
        "Name",
        "Phone",
        "TelegramChatId",
        "NotificationGroup",
        "IsAdmin",
    ];

    public async Task<List<User>> GetListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await cache.GetOrCreateAsync(
            "Users",
            (sheetsService, options),
            async (state, ct) =>
                await sheetsService
                    .Spreadsheets.Values.Get(options.Value.SpreadsheetId, "Users")
                    .ExecuteAsync(ct),
            cancellationToken: cancellationToken
        );

        if (!items.Values.First().SequenceEqual(_header))
        {
            throw new Exception("Unexpected headers for Users sheet");
        }

        return items
            .Values.Skip(1)
            .Select(
                (row, idx) =>
                    new User
                    {
                        Row = idx + 2,
                        Unit = row.ElementAtOrDefault(0) as string,
                        Name = row.ElementAtOrDefault(1) as string,
                        Phone = row.ElementAtOrDefault(2) as string,
                        TelegramChatId = row.ElementAtOrDefault(3) as string,
                        NotificationGroup = row.ElementAtOrDefault(4) as string,
                        IsAdmin = (row.ElementAtOrDefault(5) as string) == "TRUE",
                    }
            )
            .ToList();
    }

    /// <summary>
    /// Users by phone number, for looking up the users behind many bookings at once.
    /// </summary>
    public async Task<Dictionary<string, User>> GetByPhoneAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var byPhone = new Dictionary<string, User>();
        foreach (var user in await GetListAsync(cancellationToken))
        {
            if (user.Phone is not null)
            {
                byPhone.TryAdd(user.Phone, user);
            }
        }

        return byPhone;
    }

    public async Task<User?> FindAsync(
        Expression<Func<User, bool>> predicate,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.SingleOrDefault(predicate.Compile());
    }

    public async Task<User> GetAsync(
        Expression<Func<User, bool>> predicate,
        CancellationToken cancellationToken = default
    )
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
                Values =
                [
                    [
                        entity.Unit,
                        entity.Name,
                        entity.Phone,
                        entity.TelegramChatId,
                        entity.NotificationGroup,
                        entity.IsAdmin.ToString().ToUpper(),
                    ],
                ],
            },
            options.Value.SpreadsheetId,
            $"Users!A{entity.Row}:F{entity.Row}"
        );

        request.ValueInputOption = SpreadsheetsResource
            .ValuesResource
            .UpdateRequest
            .ValueInputOptionEnum
            .RAW;
        await request.ExecuteAsync(cancellationToken);

        await cache.RemoveAsync("Users", cancellationToken);

        return entity;
    }

    public async Task DeleteAsync(
        Expression<Func<User, bool>> predicate,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();
        var entity = await GetAsync(predicate, cancellationToken);

        await sheetsService
            .Spreadsheets.BatchUpdate(
                new BatchUpdateSpreadsheetRequest
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
                                    EndIndex = entity.Row,
                                },
                            },
                        },
                    ],
                },
                options.Value.SpreadsheetId
            )
            .ExecuteAsync(cancellationToken);

        await cache.RemoveAsync("Users", cancellationToken);
    }

    private async Task<int?> GetSheetId(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        // Sheet IDs don't change, so there's no need to look it up for every delete
        return await cache.GetOrCreateAsync(
            "Users Sheet ID",
            (sheetsService, options),
            async (state, ct) =>
            {
                var request = state.sheetsService.Spreadsheets.Get(
                    state.options.Value.SpreadsheetId
                );
                request.Fields = "sheets.properties(sheetId,title)";

                var sheet = await request.ExecuteAsync(ct);
                return sheet.Sheets.Single(s => s.Properties.Title == "Users").Properties.SheetId;
            },
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) },
            cancellationToken: cancellationToken
        );
    }
}
