using ConsoleAppFramework;
using Fbs.DbMigrator.Commands;
using Fbs.WebApi.Claims;
using Fbs.WebApi.Data;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

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
            services.AddScoped<MemberPromotions>();
            services.AddScoped<TenantSuspensions>();
            services.AddScoped<TenantPurges>();
        }
    )
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    });

app.Add<DiffCommand>();
app.Add<ApplyCommand>();
app.Add<PromoteAdminCommand>();
app.Add<SuspendCommand>();
app.Add<ListTenantsCommand>();
app.Add<PurgeTenantsCommand>();

await app.RunAsync(args);
