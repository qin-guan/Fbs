using System.IO.Compression;
using System.Text;
using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using Fbs.WebApi;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Data;
using Fbs.WebApi.Endpoints.Auth;
using Fbs.WebApi.Events;
using Fbs.WebApi.Middleware;
using Fbs.WebApi.Notifications;
using Fbs.WebApi.Options;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Repository.Database;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
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

            options.Cookie.Domain = ".from.sg";
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

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<GoogleOptions>>();
    var serviceAccountJsonCredential = Encoding.UTF8.GetString(
        Convert.FromBase64String(options.Value.ServiceAccountJsonCredential)
    );

    var credential = GoogleCredential
        .FromJson(serviceAccountJsonCredential)
        .CreateScoped(
            "https://www.googleapis.com/auth/calendar",
            "https://www.googleapis.com/auth/calendar.events"
        );
    var service = new CalendarService(
        new BaseClientService.Initializer { HttpClientInitializer = credential }
    );

    return service;
});

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<GoogleOptions>>();
    var serviceAccountJsonCredential = Encoding.UTF8.GetString(
        Convert.FromBase64String(options.Value.ServiceAccountJsonCredential)
    );

    var credential = GoogleCredential.FromJson(serviceAccountJsonCredential);
    var service = new SheetsService(
        new BaseClientService.Initializer { HttpClientInitializer = credential }
    );

    return service;
});

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
    builder.Services.AddSingleton<ISqlSugarClient>(_ =>
        SqlSugarClientFactory.Create(databaseConnectionString)
    );
}

builder.Services.AddSingleton<InstrumentationSource>();
builder.Services.AddSingleton<BackgroundPublisher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackgroundPublisher>());

builder.Services.AddScoped<TraceIdMiddleware>();
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
}
else
{
    builder.Services.AddSingleton<BookingCache>();
    builder.Services.AddHostedService<CacheRefreshService>();
    builder.Services.AddSingleton<BookingWriteLock>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IFacilityRepository, FacilityRepository>();
    builder.Services.AddScoped<INominalRollRepository, NominalRollRepository>();
    builder.Services.AddScoped<IOtpRepository, OtpRepository>();
    builder.Services.AddScoped<BookingRepository>();
    builder.Services.AddScoped<IBookingService, CalendarBookingService>();
}

builder.Services.AddFastEndpoints();
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
            policy.WithOrigins("https://*.3sib-fbs.from.sg");
            policy.WithOrigins("https://3sib-fbs.from.sg");
        }

        policy
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowedToAllowWildcardSubdomains();
    });
});

#endregion

var app = builder.Build();

// A version whose schema hasn't been applied yet should fail here, before it takes any traffic. The
// migrator applies it, see Fbs.DbMigrator
if (
    app.Services.GetService<ISqlSugarClient>() is { } database
    && app.Configuration.GetValue("Startup:ValidateDatabaseSchema", true)
)
{
    var problems = SchemaValidator.FindProblems(database);
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

app.UseMiddleware<TraceIdMiddleware>();

app.UseResponseCompression();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(config =>
{
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
