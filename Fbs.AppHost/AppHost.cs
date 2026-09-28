using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// Google and Telegram settings are read from the API's user secrets, see README.md
var api = builder
    .AddProject<Fbs_WebApi>("api")
    // Booking times in Telegram messages are shown in the server's local time
    .WithEnvironment("TZ", "Asia/Singapore");

builder
    .AddJavaScriptApp("app", "../Fbs.WebApp")
    .WithPnpm()
    .WithReference(api)
    // The API only allows CORS requests from this port in development
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WaitFor(api);

builder.Build().Run();
