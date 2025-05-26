namespace Fbs.WebApi.Options;

public class GoogleOptions
{
    public required string ServiceAccountJsonCredential { get; set; }
    public required string SpreadsheetId { get; set; }
    public required string CalendarId { get; set; }

    /// <summary>
    /// This is another calendar that will be in sync with the main storage calendar,
    /// but accepts manual changes, since embedded data is not read from the events in this calendar
    /// </summary>
    public required string CarbonCopyCalendarId { get; set; }
}