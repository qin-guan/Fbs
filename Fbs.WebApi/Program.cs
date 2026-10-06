using System.IO.Compression;
using System.Text;
using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using Fbs.WebApi;
using Fbs.WebApi.Auth.Clerk;
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
    // Singleton on purpose: a scope per request is what SqlSugarScope already does. Hosted services are
    // started on this context, so they go through SqlSugarContext rather than using it directly
    builder.Services.AddSingleton<ISqlSugarClient>(_ =>
        SqlSugarClientFactory.Create(databaseConnectionString)
    );

    // A version that can't reach the database isn't ready to take over from the one that is running
    builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

    // What is waiting to be sent, and how many organisations and people there are, looked at every so often for the metrics
    builder.Services.Configure<DatabaseGaugesOptions>(builder.Configuration.GetSection("Metrics:DatabaseGauges"));
    builder.Services.AddHostedService<DatabaseGaugesService>();
}

// What is counted and timed, which is sent wherever OTEL_EXPORTER_OTLP_ENDPOINT says once it is named here
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

    // What is to be done once a change is saved, such as telling people about it. It is written in the same
    // transaction as the change, so the events the endpoints publish are no longer wanted
    builder.Services.Configure<EventOptions>(options => options.Enabled = false);
    builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
    builder.Services.AddSingleton<OutboxSignal>();
    builder.Services.AddSingleton<OutboxDispatcher>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
    builder.Services.AddSingleton<TelegramThrottle>();
    builder.Services.AddScoped<IOutboxHandler, TelegramBookingNotifier>();
    builder.Services.AddScoped<IOutboxHandler, CalendarBookingSync>();
    builder.Services.Configure<CalendarSyncOptions>(builder.Configuration.GetSection("CalendarSync"));
    builder.Services.AddHostedService<CalendarReconciler>();

    // Signing in with Clerk, alongside the Telegram code and cookie that are still what the app uses. Whoever
    // signs in becomes an account, which belongs to no organisation until they are a member of one
    if (ClerkAuthentication.IsEnabled(builder.Configuration))
    {
        builder.Services.AddClerkAuthentication(builder.Configuration);
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
    if (ClerkAuthentication.IsEnabled(builder.Configuration))
    {
        throw new InvalidOperationException("Clerk needs Storage:Provider=Database: accounts and organisations are kept in it.");
    }

    builder.Services.AddGoogleStorage();
    builder.Services.AddHostedService<CacheRefreshService>();
}

// What is for people signed in with Clerk isn't there unless that is on, as it needs accounts to be
var clerkEnabled = ClerkAuthentication.IsEnabled(builder.Configuration);
builder.Services.AddFastEndpoints(options =>
    options.Filter = type => clerkEnabled || !Attribute.IsDefined(type, typeof(RequiresClerkAttribute))
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
            policy.WithOrigins("https://api.bookaspace.dev");
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

// First, so everything after it sees the address of the client rather than of the proxy
app.UseForwardedHeaders();

app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ReadOnlyMiddleware>();

app.UseResponseCompression();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// After authorization, which is what says who somebody is, as some limits are for a person rather than an address
app.UseRateLimiter();

app.UseFastEndpoints(config =>
{
    // Roles and statuses are said in words, so the app doesn't have to know what the numbers are
    config.Serializer.Options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
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
