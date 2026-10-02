using System.Diagnostics;
using Fbs.WebApi.Tests.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Fbs.WebApi.Tests;

/// <summary>
/// What the database does, seen the way an exporter sees it: each statement a request runs is a span in that request's trace, and
/// the connection pool is counted, so it is possible to tell a slow request from a slow database and a busy pool from an idle one.
/// </summary>
public class DatabaseTelemetryTests
{
    [ClassDataSource<ExportedFactory>]
    public required ExportedFactory Factory { get; init; }

    /// <summary>The API with what it exports kept in memory, as it would otherwise be sent where <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> says.</summary>
    public class ExportedFactory : ClerkFbsApiFactory
    {
        public List<Activity> Spans { get; } = [];

        public List<Metric> Metrics { get; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(Spans)).WithMetrics(metrics => metrics.AddInMemoryExporter(Metrics))
            );
        }

        /// <summary>The span of a request, which ends, and so is exported, a moment after the client has its answer.</summary>
        public async Task<Activity> RequestSpanAsync(string path)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var found = Spans.ToList().FirstOrDefault(s => s.Source.Name == "Microsoft.AspNetCore" && s.DisplayName.Contains(path));
                if (found is not null)
                {
                    return found;
                }

                await Task.Delay(50);
            }

            throw new TimeoutException($"No span was exported for {path}.");
        }

        /// <summary>What has been measured so far, which is otherwise only collected every so often.</summary>
        public IReadOnlyList<Metric> ExportedMetrics()
        {
            Services.GetRequiredService<MeterProvider>().ForceFlush();
            return Metrics.ToList();
        }
    }

    [Test]
    public async Task A_request_that_reads_the_database_has_the_statements_in_its_trace()
    {
        using var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        (await client.GetAsync("/Me")).EnsureSuccessStatusCode();

        var request = await Factory.RequestSpanAsync("/Me");
        var spans = Factory.Spans.ToList();
        var statements = spans.Where(s => s.Source.Name == "MySqlConnector" && s.TraceId == request.TraceId).ToList();
        await Assert.That(statements).IsNotEmpty();
        await Assert.That(statements.Any(s => s.GetTagItem("db.statement") is string text && text.Contains("SELECT", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task The_connection_pool_is_counted()
    {
        using var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        (await client.GetAsync("/Me")).EnsureSuccessStatusCode();

        var names = Factory.ExportedMetrics().Select(m => m.Name).ToHashSet();

        await Assert.That(names).Contains("db.client.connections.usage");
        await Assert.That(names).Contains("db.client.connections.max");
    }
}
