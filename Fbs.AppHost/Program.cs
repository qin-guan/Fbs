using System.Diagnostics;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Fbs_WebApi>("api")
    .WithEnvironment("TZ", "Asia/Singapore");
var app = builder.AddNpmApp("app", "../Fbs.WebApp", "dev")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();

foreach (var proc in Process.GetProcessesByName("npm run dev"))
{
    Console.WriteLine($"Killing {proc.ProcessName} {proc.Id}");
    proc.Kill(true);
}

foreach (var proc in Process.GetProcessesByName("Fbs.WebApi"))
{
    Console.WriteLine($"Killing {proc.ProcessName} {proc.Id}");
    proc.Kill(true);
}

foreach (var proc in Process.GetProcessesByName("Aspire.Dashboard"))
{
    Console.WriteLine($"Killing {proc.ProcessName} {proc.Id}");
    proc.Kill(true);
}
