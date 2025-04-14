using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Services.GoogleSheets;

public class GoogleSheetsUsersService(
    ILogger<GoogleSheetsUsersService> logger,
    IOptions<GoogleOptions> options,
    SheetsService sheetsService
)
{
    public async Task<IList<User>> GetAsync()
    {
        var values = await sheetsService.Spreadsheets.Values
            .Get(options.Value.SpreadsheetId, "Users")
            .ExecuteAsync();

        ArgumentNullException.ThrowIfNull(values);

        if (values.Values.First() is not ["Name", "Phone", "TelegramChatId"])
        {
            throw new Exception("Sheet headers are not correct.");
        }

        return values.Values
            .Skip(1)
            .Select((r, idx) => new User
            {
                Row = idx + 2,
                Name = r.ElementAtOrDefault(0) as string ?? string.Empty,
                Phone = r.ElementAtOrDefault(1) as string ?? string.Empty,
                TelegramChatId = r.ElementAtOrDefault(2) as string ?? string.Empty,
            })
            .ToList();
    }

    public async Task SetTelegramChatIdAsync(int row, long telegramChatId)
    {
        var update = sheetsService.Spreadsheets.Values.Update(
            new ValueRange
            {
                Values = [[telegramChatId]],
            },
            options.Value.SpreadsheetId,
            $"Users!C{row}:C{row}"
        );

        update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        var res = await update.ExecuteAsync();
    }
}