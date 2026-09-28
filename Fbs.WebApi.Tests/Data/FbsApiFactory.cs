using System.Security.Claims;
using System.Text.Encodings.Web;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Tests.Fakes;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// Runs the real API in memory, with Google Sheets, Google Calendar and Telegram faked at the HTTP level.
/// </summary>
/// <remarks>
/// Inject with <c>[ClassDataSource&lt;FbsApiFactory&gt;]</c> to get a fresh app and fakes for every test. The app
/// starts on first use, so the fakes can be set up before then.
/// </remarks>
public class FbsApiFactory : WebApplicationFactory<Program>
{
    public FakeGoogle Google { get; } = new();
    public FakeTelegram Telegram { get; } = new();

    public HttpClient CreateClientFor(string phone)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.PhoneHeader, phone);
        return client;
    }

    /// <summary>
    /// Adds a booking to the calendar and waits for the API to pick it up, as if it was made
    /// before the test.
    /// </summary>
    public async Task AddBookingAsync(Booking booking)
    {
        Google.AddBooking(booking);
        await Services.GetRequiredService<BookingCache>().SyncAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Google:ServiceAccountJsonCredential", "unused");
        builder.UseSetting("Google:SpreadsheetId", "spreadsheet");
        builder.UseSetting("Google:CalendarId", FakeGoogle.MainCalendar);
        builder.UseSetting("Google:CarbonCopyCalendarId", FakeGoogle.CarbonCopyCalendar);
        builder.UseSetting("Telegram:Token", "123456:test-token");
        builder.UseSetting("Telegram:WebhookUrl", "https://fbs.test/Bot");

        builder.ConfigureTestServices(services =>
        {
            var initializer = new BaseClientService.Initializer
            {
                ApplicationName = "Fbs.WebApi.Tests",
                HttpClientFactory = Google.CreateHttpClientFactory(),
            };
            services.RemoveAll<CalendarService>();
            services.AddSingleton(new CalendarService(initializer));
            services.RemoveAll<SheetsService>();
            services.AddSingleton(new SheetsService(initializer));

            services
                .AddHttpClient("tgwebhook")
                .ConfigurePrimaryHttpMessageHandler(() => Telegram.CreateHandler());

            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.Scheme;
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        });
    }
}

/// <summary>
/// Signs requests in as the phone number in the <c>X-Test-Phone</c> header.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";
    public const string PhoneHeader = "X-Test-Phone";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PhoneHeader, out var phone))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim("Phone", phone.ToString())], Scheme);
        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme))
        );
    }
}
