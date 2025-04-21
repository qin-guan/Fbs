using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Fbs_WebApi>("api")
    .WithEnvironment("TZ", "Asia/Singapore");
var app = builder.AddNpmApp("app", "../Fbs.WebApp", "dev")
    .WithReference(api);

builder.Build().Run();