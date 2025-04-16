using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Fbs_WebApi>("api");
var app = builder.AddNpmApp("app", "../Fbs.WebApp", "dev")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();