using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// The database everything is moving to. TiDB, as in production, because it reads inside transactions differently to
// MySQL, see docs/adr/spikes/s3-tidb-concurrency. It is ready when its status endpoint answers, which is after the
// port it is reached on starts accepting connections. The tests start this too, see TestDatabase
var tidb = builder
    .AddContainer("tidb", "pingcap/tidb", "v8.5.8")
    .WithEndpoint(targetPort: 4000, name: "mysql")
    .WithHttpEndpoint(targetPort: 10080, name: "status")
    .WithHttpHealthCheck("/status", endpointName: "status");
var mysql = tidb.GetEndpoint("mysql");
var db = builder.AddConnectionString(
    "db",
    ReferenceExpression.Create($"Server={mysql.Property(EndpointProperty.Host)};Port={mysql.Property(EndpointProperty.Port)};User ID=root;Database=fbs")
);

// Applies the schema before the API starts, which refuses to start on a database that hasn't got it
var migrator = builder
    .AddProject<Fbs_DbMigrator>("migrator")
    .WithArgs("apply", "--create-database")
    .WithReference(db)
    .WaitFor(tidb);

// Google and Telegram settings are read from the API's user secrets, see README.md. It reads and writes Google
// until Storage__Provider=Database, which moves it all to the database above
var api = builder
    .AddProject<Fbs_WebApi>("api")
    .WithReference(db)
    .WaitForCompletion(migrator)
    // Booking times in Telegram messages are shown in the server's local time, until Storage__Provider=Database,
    // which shows them in the tenant's
    .WithEnvironment("TZ", "Asia/Singapore");

builder
    .AddJavaScriptApp("app", "../Fbs.WebApp")
    .WithPnpm()
    .WithReference(api)
    // The API only allows CORS requests from this port in development
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WaitFor(api);

builder.Build().Run();
