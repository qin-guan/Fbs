using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Fbs_WebApi>("api");

builder.Build().Run();