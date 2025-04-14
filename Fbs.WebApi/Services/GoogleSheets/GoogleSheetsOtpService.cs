using Fbs.WebApi.Options;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Services.GoogleSheets;

public class GoogleSheetsOtpService(
    IOptions<GoogleOptions> options,
    SheetsService sheetsService
)
{
    private const string SheetName = "OTPs";

    public async Task<Entities.Otp?> GetOtpAsync(string phone)
    {
        var res = await sheetsService.Spreadsheets.Values.Get(options.Value.SpreadsheetId, SheetName).ExecuteAsync();
        return res.Values
            .Skip(1)
            .Select((v, idx) => new Entities.Otp
            {
                Row = idx + 2,
                Phone = v.ElementAtOrDefault(0) as string ?? string.Empty,
                Code = v.ElementAtOrDefault(1) as string ?? string.Empty,
                CreatedAt = DateTimeOffset.Parse(v.ElementAtOrDefault(2) as string ?? string.Empty),
            })
            .SingleOrDefault();
    }

    public async Task<Entities.Otp> SetOtpAsync(string phone, string code)
    {
        var existing = await GetOtpAsync(phone);
        if (existing is not null)
        {
            var req = sheetsService.Spreadsheets.Values.Update(
                new ValueRange
                {
                    Values = [[phone, code, DateTimeOffset.Now.ToString()]]
                },
                options.Value.SpreadsheetId,
                $"{SheetName}!A{existing.Row}:C{existing.Row}"
            );

            req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;

            await req.ExecuteAsync();
        }
        else
        {
            var req = sheetsService.Spreadsheets.Values.Append(
                new ValueRange
                {
                    Values = [[phone, code, DateTimeOffset.Now.ToString()]]
                },
                options.Value.SpreadsheetId,
                $"{SheetName}"
            );

            req.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;

            await req.ExecuteAsync();
        }

        return await GetOtpAsync(phone);
    }

    public async Task RemoveOtpAsync(string phone)
    {
        var otp = await GetOtpAsync(phone);
        ArgumentNullException.ThrowIfNull(otp);

        await sheetsService.Spreadsheets.Values.Clear(
            new ClearValuesRequest(),
            options.Value.SpreadsheetId,
            $"{SheetName}!A{otp.Row}:C"
        ).ExecuteAsync();
    }
}