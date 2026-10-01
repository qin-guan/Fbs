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
    /// <summary>The secret the API registers with Telegram and expects on bot updates.</summary>
    public const string TelegramWebhookSecret = "test-webhook-secret-0123456789";

    /// <summary>The hosting environment the API runs in.</summary>
    protected virtual string EnvironmentName => "Development";

    public FakeGoogle Google { get; } = new();
    public FakeTelegram Telegram { get; } = new();

    /// <summary>Adds someone to the users, as if they were there before the test.</summary>
    public virtual void AddUser(string unit, string name, string phone, string? telegramChatId, string notificationGroup, bool isAdmin = false) =>
        Google.Sheets["Users"].Add([unit, name, phone, telegramChatId ?? "", notificationGroup, isAdmin ? "TRUE" : "FALSE"]);

    /// <summary>The Telegram chat the user has linked, as stored.</summary>
    public virtual string? TelegramChatIdOf(string phone) => Google.Sheets["Users"].Single(row => row[2] == phone)[3];

    /// <summary>How many login codes are stored.</summary>
    public virtual int StoredCodeCount => Google.Sheets["OTPs"].Count - 1;

    /// <summary>Makes the login code that was sent to the phone look like it was sent a while ago.</summary>
    public virtual void AgeCode(string phone, TimeSpan age) =>
        Google.Sheets["OTPs"].Single(row => row[0] == phone)[2] = DateTimeOffset.UtcNow.Subtract(age).ToString();

    public HttpClient CreateClientFor(string phone)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.PhoneHeader, phone);
        return client;
    }

    /// <summary>
    /// Adds a booking where bookings are kept, and waits for the API to pick it up, as if it was made
    /// before the test.
    /// </summary>
    public virtual async Task AddBookingAsync(Booking booking)
    {
        Google.AddBooking(booking);
        await Services.GetRequiredService<BookingCache>().SyncAsync();
    }

    /// <summary>Adds a lot of bookings before the test starts, quicker than one at a time.</summary>
    public virtual void AddBookings(IReadOnlyList<Booking> bookings)
    {
        foreach (var booking in bookings)
        {
            Google.AddBooking(booking);
        }
    }

    /// <summary>
    /// How many bookings each place that keeps them holds, which is the same for all of them unless
    /// something has gone wrong. Cancelled bookings aren't counted.
    /// </summary>
    public virtual IReadOnlyList<int> StoredBookingCounts =>
        [Google.Events(FakeGoogle.MainCalendar).Count, Google.Events(FakeGoogle.CarbonCopyCalendar).Count];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("Google:ServiceAccountJsonCredential", "unused");
        builder.UseSetting("Google:SpreadsheetId", "spreadsheet");
        builder.UseSetting("Google:CalendarId", FakeGoogle.MainCalendar);
        builder.UseSetting("Google:CarbonCopyCalendarId", FakeGoogle.CarbonCopyCalendar);
        builder.UseSetting("Telegram:Token", "123456:test-token");
        builder.UseSetting("Telegram:WebhookUrl", "https://fbs.test/Bot");
        builder.UseSetting("Telegram:WebhookSecret", TelegramWebhookSecret);

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
/// The same API running as it does when deployed, for things that only differ in production.
/// </summary>
public class ProductionFbsApiFactory : FbsApiFactory
{
    protected override string EnvironmentName => "Production";
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
