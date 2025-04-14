using Fbs.WebApi.Entities;
using Fbs.WebApi.Options;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Services.GoogleSheets;

public class GoogleSheetsFacilitiesService(
    ILogger<GoogleSheetsFacilitiesService> logger,
    IOptions<GoogleOptions> options,
    SheetsService sheetsService
)
{
    private const string SheetName = "Facilities";

    public async Task<List<Facility>> GetFacilitiesAsync()
    {
        var res = await sheetsService.Spreadsheets.Values.Get(options.Value.SpreadsheetId, SheetName).ExecuteAsync();
        return res.Values
            .Skip(1)
            .Select((v, idx) => new Facility
            {
                Row = idx + 2,
                Name = v.ElementAtOrDefault(0) as string ?? string.Empty
            })
            .ToList();
    }
}