using ConsoleAppFramework;
using Fbs.DbMigrator.Commands;
using Fbs.WebApi.Data;
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
        }
    )
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    });

app.Add<DiffCommand>();
app.Add<ApplyCommand>();

await app.RunAsync(args);
