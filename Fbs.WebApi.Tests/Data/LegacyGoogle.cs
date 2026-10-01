using Fbs.WebApi.Legacy;
using Fbs.WebApi.Options;
using Fbs.WebApi.Tests.Fakes;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace Fbs.WebApi.Tests.Data;

public static class LegacyGoogle
{
    /// <summary>
    /// The legacy stores reading a fake Google, wired the way the API and the migrator wire them, and made
    /// afresh each time as they are for each run of a command, so nothing is left cached from before.
    /// </summary>
    public static ServiceProvider Services(FakeGoogle google)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<GoogleOptions>().Configure(o =>
        {
            o.ServiceAccountJsonCredential = "unused";
            o.SpreadsheetId = "spreadsheet";
            o.CalendarId = FakeGoogle.MainCalendar;
            o.CarbonCopyCalendarId = FakeGoogle.CarbonCopyCalendar;
        });
        var initializer = new BaseClientService.Initializer { ApplicationName = "Tests", HttpClientFactory = google.CreateHttpClientFactory() };
        services.AddSingleton(new CalendarService(initializer));
        services.AddSingleton(new SheetsService(initializer));
        services.AddFusionCache().AsHybridCache();
        services.AddGoogleStorage();
        return services.BuildServiceProvider();
    }
}
