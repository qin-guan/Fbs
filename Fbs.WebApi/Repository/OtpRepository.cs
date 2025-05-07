using System.Linq.Expressions;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Repository;

public class OtpRepository(
    InstrumentationSource instrumentation,
    IOptions<GoogleOptions> options,
    SheetsService sheetsService
) : IRepository<Otp>
{
    private readonly string[] _header =
    [
        "Phone", "Code", "CreatedAt"
    ];

    public async Task<List<Otp>> GetListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();
        
        var items = await sheetsService.Spreadsheets.Values.Get(options.Value.SpreadsheetId, "OTPs")
            .ExecuteAsync(cancellationToken);

        if (!items.Values.First().SequenceEqual(_header))
        {
            throw new Exception("Unexpected headers for Otp sheet");
        }

        return items.Values
            .Skip(1)
            .Select((row, idx) => new Otp
            {
                Row = idx + 2,
                Phone = row.ElementAtOrDefault(0) as string,
                Code = row.ElementAtOrDefault(1) as string,
                CreatedAt = row.ElementAtOrDefault(2) is string o ? DateTimeOffset.Parse(o) : null,
            })
            .ToList();
    }

    public async Task<Otp?> FindAsync(Expression<Func<Otp, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.SingleOrDefault(predicate.Compile());
    }

    public async Task<Otp> GetAsync(Expression<Func<Otp, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var items = await GetListAsync(cancellationToken);
        return items.Single(predicate.Compile());
    }

    public async Task<Otp> InsertAsync(Otp entity, CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var r = sheetsService.Spreadsheets.Values.Append(
            new ValueRange
            {
                Values = [[entity.Phone, entity.Code, entity.CreatedAt.ToString()]]
            },
            options.Value.SpreadsheetId,
            "OTPs"
        );

        r.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;

        await r.ExecuteAsync(cancellationToken);

        return await GetAsync(o => o.Phone == entity.Phone, cancellationToken);
    }

    public async Task<Otp> UpdateAsync(Otp entity, CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var request = sheetsService.Spreadsheets.Values.Update(
            new ValueRange
            {
                Values = [[entity.Phone, entity.Code, entity.CreatedAt.ToString()]]
            },
            options.Value.SpreadsheetId,
            $"OTPs!A{entity.Row}:C{entity.Row}"
        );

        request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await request.ExecuteAsync(cancellationToken);

        return entity;
    }

    public async Task DeleteAsync(Expression<Func<Otp, bool>> predicate, CancellationToken cancellationToken = default)
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
    }

    private async Task<int?> GetSheetId(CancellationToken cancellationToken = default)
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var sheet = await sheetsService.Spreadsheets.Get(options.Value.SpreadsheetId).ExecuteAsync(cancellationToken);
        return sheet.Sheets.Single(s => s.Properties.Title == "OTPs").Properties.SheetId;
    }
}