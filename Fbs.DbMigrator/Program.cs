using ConsoleAppFramework;
using Fbs.DbMigrator.Commands;
using Fbs.WebApi.Claims;
using Fbs.WebApi.Data;
using Fbs.WebApi.Legacy;
using Fbs.WebApi.Options;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using ZiggyCreatures.Caching.Fusion;

var app = ConsoleApp
    .Create()
    .ConfigureEmptyConfiguration(configure => configure.AddEnvironmentVariables())
    .ConfigureServices(
        (configuration, services) =>
        {
            var connectionString =
                configuration.GetConnectionString("db")
                ?? throw new InvalidOperationException("ConnectionStrings:db is required.");

            services.AddSingleton(new DatabaseTarget(connectionString));
            services.AddSingleton<ISqlSugarClient>(_ => SqlSugarClientFactory.Create(connectionString));

            // Only used by the commands that read Google, which is why nothing here needs Google__* set
            // for diff or apply: none of it is made until it is asked for
            services
                .AddOptions<GoogleOptions>()
                .Bind(configuration.GetSection("Google"))
                .Validate(
                    options =>
                        !string.IsNullOrWhiteSpace(options.ServiceAccountJsonCredential)
                        && !string.IsNullOrWhiteSpace(options.SpreadsheetId)
                        && !string.IsNullOrWhiteSpace(options.CalendarId),
                    "Google:ServiceAccountJsonCredential, Google:SpreadsheetId and Google:CalendarId are required to read Google."
                );
            services.AddGoogleClients();
            services.AddFusionCache().AsHybridCache();
            services.AddGoogleStorage();
            services.AddScoped<LegacyImporter>();
            services.AddScoped<LegacyVerifier>();
            services.AddScoped<LegacyExporter>();
            services.AddScoped<MemberPromotions>();
            services.AddScoped<TenantSuspensions>();
        }
    )
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    });

app.Add<DiffCommand>();
app.Add<ApplyCommand>();
app.Add<ImportLegacyCommand>();
app.Add<VerifyLegacyCommand>();
app.Add<ExportLegacyCommand>();
app.Add<PromoteAdminCommand>();
app.Add<SuspendCommand>();
app.Add<ListTenantsCommand>();

await app.RunAsync(args);
