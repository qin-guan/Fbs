using System.Security.Claims;
using System.Text.Encodings.Web;
using Fbs.WebApi.Tests.Fakes;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// Runs the real API in memory, with Google Calendar and Telegram faked at the HTTP level, and no database, so it
/// doesn't start: <see cref="DatabaseFbsApiFactory"/> is the one with a database.
/// </summary>
/// <remarks>
/// The app starts on first use, so the fakes can be set up before then.
/// </remarks>
public class FbsApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The secret the API registers with Telegram and expects on bot updates.</summary>
    public const string TelegramWebhookSecret = "test-webhook-secret-0123456789";

    /// <summary>The hosting environment the API runs in.</summary>
    protected virtual string EnvironmentName => "Development";

    public FakeGoogle Google { get; } = new();
    public FakeTelegram Telegram { get; } = new();

    public HttpClient CreateClientFor(string phone)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.PhoneHeader, phone);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("Google:ServiceAccountJsonCredential", "unused");
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

            // Without the retries every HTTP client gets, which would make a failure from the fake take
            // seconds to show, and are not what these tests are about
#pragma warning disable EXTEXP0001 // Experimental, but only used here, to stand in for what is being kept out of these tests
            services
                .AddHttpClient("tgwebhook")
                .ConfigurePrimaryHttpMessageHandler(() => Telegram.CreateHandler())
                .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

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
public class ProductionFbsApiFactory : DatabaseFbsApiFactory
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
