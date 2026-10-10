using System.IO.Compression;
using System.Text;
using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using Fbs.WebApi;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Auth.WorkOS;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Data;
using Fbs.WebApi.Endpoints.Auth;
using Fbs.WebApi.Events;
using Fbs.WebApi.Health;
using Fbs.WebApi.Legacy;
using Fbs.WebApi.Middleware;
using Fbs.WebApi.Notifications;
using Fbs.WebApi.Options;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Repository;
using Fbs.WebApi.RateLimiting;
using Fbs.WebApi.Repository.Database;
using Fbs.WebApi.Telemetry;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;
using SqlSugar;
using Telegram.Bot;
using ZiggyCreatures.Caching.Fusion;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

#region Options

builder
    .Services.AddOptions<GoogleOptions>()
    .Bind(builder.Configuration.GetSection("Google"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ServiceAccountJsonCredential));

builder
    .Services.AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection("Telegram"))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.Token)
            && !string.IsNullOrWhiteSpace(options.WebhookUrl)
            && TelegramOptions.IsValidWebhookSecret(options.WebhookSecret),
        "Telegram:Token, Telegram:WebhookUrl and Telegram:WebhookSecret are required. "
            + "The secret must be 16-256 characters of A-Z, a-z, 0-9, '_' or '-'."
    );

#endregion

#region Services

builder
    .Services.AddAuthenticationCookie(
        validFor: TimeSpan.FromDays(1),
        options =>
        {
            if (!builder.Environment.IsProduction())
                return;

            options.Cookie.Domain = ".bookaspace.app";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
    )
    .AddAuthorization();

// Auth cookies are encrypted with the Data Protection key ring, which by default lives inside the
// container and is regenerated on every redeploy, logging everyone out. Point this at a persistent
// volume to keep sessions across deploys.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder
        .Services.AddDataProtection()
        .SetApplicationName("Fbs.WebApi")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder
    .Services.AddHttpClient<TelegramBotClient>("tgwebhook")
    .AddTypedClient(
        (httpClient, sp) =>
            new TelegramBotClient(
                sp.GetRequiredService<IOptions<TelegramOptions>>().Value.Token,
                httpClient
            )
    );

builder.Services.AddGoogleClients();

builder
    .Services.AddFusionCache()
    .WithDefaultEntryOptions(
        new FusionCacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(30),
            // Once loaded, keep serving the last copy while it's refreshed in the background
            // instead of making requests wait on Google Sheets
            IsFailSafeEnabled = true,
            FactorySoftTimeout = TimeSpan.FromMilliseconds(100),
            EagerRefreshThreshold = 0.8f,
        }
    )
    .AsHybridCache();
// The database is only there once a connection string is configured, and is only used once
// Storage:Provider says Database
if (builder.Configuration.GetConnectionString("db") is { Length: > 0 } databaseConnectionString)
{
    // Singleton on purpose: SqlSugarScope already scopes a client per request. Hosted services are
    // started on this context, so they go through SqlSugarContext. Using this client directly would share it.
    builder.Services.AddSingleton<ISqlSugarClient>(_ =>
        SqlSugarClientFactory.Create(databaseConnectionString)
    );

    // A version that can't reach the database isn't ready to take over from the one that is running
    builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

    // How many messages are waiting to be sent, and how many organisations and people there are. Sampled on a timer for the metrics.
    builder.Services.Configure<DatabaseGaugesOptions>(builder.Configuration.GetSection("Metrics:DatabaseGauges"));
    builder.Services.AddHostedService<DatabaseGaugesService>();
}

// Counters and timers from FbsMetrics. Exported wherever OTEL_EXPORTER_OTLP_ENDPOINT points, once the meter is named here.
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(FbsMetrics.MeterName));

builder.Services.AddSingleton<InstrumentationSource>();
builder.Services.AddSingleton<BackgroundPublisher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackgroundPublisher>());

builder.Services.AddScoped<TraceIdMiddleware>();
builder.Services.AddScoped<ReadOnlyMiddleware>();
builder.Services.AddSingleton<OtpAttemptTracker>();
// Bookings, users, facilities, the roster and login codes are kept in Google (bookings in Google Calendar,
// the rest in Google Sheets), until Storage:Provider says Database
if (string.Equals(builder.Configuration["Storage:Provider"], "Database", StringComparison.OrdinalIgnoreCase))
{
    if (builder.Configuration.GetConnectionString("db") is not { Length: > 0 })
    {
        throw new InvalidOperationException("Storage:Provider is Database, which needs ConnectionStrings:db.");
    }

    builder.Services.AddSingleton<DefaultTenant>();
    builder.Services.AddScoped<IUserRepository, DatabaseUserRepository>();
    builder.Services.AddScoped<IFacilityRepository, DatabaseFacilityRepository>();
    builder.Services.AddScoped<INominalRollRepository, DatabaseNominalRollRepository>();
    builder.Services.AddScoped<IOtpRepository, DatabaseOtpRepository>();
    builder.Services.AddScoped<IBookingService, DatabaseBookingService>();

    // Work that runs after a change is saved, such as telling people about it. It is written in the same
    // transaction as the change, so the events the endpoints publish are turned off.
    builder.Services.Configure<EventOptions>(options => options.Enabled = false);
    builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
    builder.Services.AddSingleton<OutboxSignal>();
    builder.Services.AddSingleton<OutboxDispatcher>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
    builder.Services.AddSingleton<TelegramThrottle>();
    builder.Services.AddScoped<IOutboxHandler, TelegramBookingNotifier>();
    builder.Services.AddScoped<IOutboxHandler, CalendarBookingSync>();
    builder.Services.AddScoped<CalendarConnector>();
    builder.Services.Configure<CalendarSyncOptions>(builder.Configuration.GetSection("CalendarSync"));
    builder.Services.AddHostedService<CalendarReconciler>();

    // Signing in with an account from Clerk or WorkOS, or both while people move from one to the other, alongside the
    // Telegram code and cookie that the old app uses. Whoever signs in becomes an account, which belongs to no
    // organisation until they are a member of one
    if (AccountAuthentication.IsEnabled(builder.Configuration))
    {
        builder.Services.AddAccountAuthentication(builder.Configuration);
    }

    // Until there are screens to manage people and facilities in, the sheets can stay where they are edited
    if (builder.Configuration.GetValue<bool>("ReferenceData:Sheets:Enabled"))
    {
        builder.Services.Configure<SheetsSyncOptions>(builder.Configuration.GetSection("ReferenceData:Sheets"));
        builder.Services.AddScoped<UserRepository>();
        builder.Services.AddScoped<FacilityRepository>();
        builder.Services.AddScoped<NominalRollRepository>();
        builder.Services.AddScoped<SheetsReferenceSync>();
        builder.Services.AddHostedService<SheetsReferenceSyncService>();
    }
}
else
{
    if (AccountAuthentication.IsEnabled(builder.Configuration))
    {
        throw new InvalidOperationException("Clerk and WorkOS need Storage:Provider=Database: accounts and organisations are kept in it.");
    }

    builder.Services.AddGoogleStorage();
    builder.Services.AddHostedService<CacheRefreshService>();
}

// Endpoints for people signed in with an account are registered only when Clerk or WorkOS is on, as they need accounts, and
// each one's webhook only when it is on
var accountsEnabled = AccountAuthentication.IsEnabled(builder.Configuration);
var clerkEnabled = ClerkAuthentication.IsEnabled(builder.Configuration);
var workOSEnabled = WorkOSAuthentication.IsEnabled(builder.Configuration);
builder.Services.AddFastEndpoints(options =>
    options.Filter = type =>
        (accountsEnabled || !Attribute.IsDefined(type, typeof(RequiresAccountsAttribute)))
        && (clerkEnabled || !Attribute.IsDefined(type, typeof(RequiresClerkAttribute)))
        && (workOSEnabled || !Attribute.IsDefined(type, typeof(RequiresWorkOSAttribute)))
);
builder.Services.SwaggerDocument(options =>
{
    options.EndpointFilter = ep => ep.EndpointTags?.Contains("Telegram") is false or null;
});

// Booking lists grow with every booking and compress well
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ["application/json", "application/problem+json"];
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest
);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest
);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins("http://localhost:3000");
            policy.WithOrigins("https://*.asse.devtunnels.ms");
        }
        else
        {
            policy.WithOrigins("https://*.3sib-fbs.pages.dev");
            policy.WithOrigins("https://3sib-fbs.pages.dev");
            policy.WithOrigins("https://bookaspace.app");
        }

        policy
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowedToAllowWildcardSubdomains();
    });
});

#endregion

// Behind a proxy such as Coolify's, the address and scheme a request came with are the proxy's, and the client's are in
// headers that are only believed from a proxy that is in a private range
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddFbsRateLimiting(builder.Configuration);

var app = builder.Build();

// A version whose schema hasn't been applied yet should fail here, before it takes any traffic. The
// migrator applies it, see Fbs.DbMigrator
if (
    app.Services.GetService<ISqlSugarClient>() is { } database
    && app.Configuration.GetValue("Startup:ValidateDatabaseSchema", true)
)
{
    // Off this context, so the client it opens is not the one every hosted service would otherwise inherit
    var problems = await SqlSugarContext.RunIsolatedAsync(() => SchemaValidator.FindProblems(database));
    if (problems.Count > 0)
    {
        app.Logger.LogCritical(
            "Database schema does not match what this version needs: {Problems}",
            string.Join("; ", problems)
        );
        throw new InvalidOperationException(
            "Database schema does not match what this version needs. Apply it with Fbs.DbMigrator before starting the API. "
                + string.Join("; ", problems)
        );
    }

    app.Logger.LogInformation("Database schema validation passed.");
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var client = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<TelegramOptions>>();
    await client.SetWebhook(
        options.Value.WebhookUrl,
        secretToken: options.Value.WebhookSecret
    );
}

// First. Later middleware should see the client's address. Before this, the address is the proxy's.
app.UseForwardedHeaders();

app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ReadOnlyMiddleware>();

app.UseResponseCompression();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// After authorization. Some limits follow the person authorization identified, and some follow the address.
app.UseRateLimiter();

app.UseFastEndpoints(config =>
{
    // Roles and statuses carry JsonStringEnumConverter on the enum. A converter registered here
    // applies first and would also take Telegram's enums, so a /start update (entity type
    // "bot_command") fails to bind and the webhook answers 400.
    config.Errors.UseProblemDetails(c =>
    {
        c.IndicateErrorCode = true;
    });
});
app.UseSwaggerGen(config =>
{
    config.Path = "/openapi/{documentName}.json";
});

app.MapDefaultEndpoints();
app.MapScalarApiReference();

await app.RunAsync();
