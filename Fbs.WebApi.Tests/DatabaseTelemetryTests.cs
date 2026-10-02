using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Fbs.WebApi.Tests;

/// <summary>
/// What the database does, seen the way an exporter sees it: each statement a request runs is a span in that request's trace,
/// and the connection pool is counted, so it is possible to tell a slow request from a slow database and a busy pool from an
/// idle one. And that the SQL, which has some of the values of a batch of inserts in it, is not in what is sent.
/// </summary>
public class DatabaseTelemetryTests
{
    // What somebody typed into a booking, which is kept in parameters by the version of SqlSugar there is, and has to be nowhere in
    // what is sent whichever way it is kept
    private const string PocName = "Wanda Leakproof";
    private const string PocPhone = "+6591230000";
    private const string Description = "Bring the keys to the back door";

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

    /// <summary>
    /// The API sending to a collector of its own, which keeps what it is sent: the whole way, through the exporter, and not
    /// only as far as the spans are made.
    /// </summary>
    public class CollectedFactory : ClerkFbsApiFactory
    {
        private readonly HttpListener _listener = new();
        private readonly List<byte[]> _received = [];
        private readonly string _endpoint;

        public CollectedFactory()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _endpoint = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add($"{_endpoint}/");
            _listener.Start();
            _ = Task.Run(ListenAsync);
        }

        /// <summary>Each of the bodies that came to <c>/v1/traces</c>.</summary>
        public IReadOnlyList<byte[]> Traces
        {
            get
            {
                lock (_received)
                {
                    return _received.ToList();
                }
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", _endpoint);
            builder.UseSetting("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        }

        public void Flush() => Services.GetRequiredService<TracerProvider>().ForceFlush();

        private async Task ListenAsync()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    using var body = new MemoryStream();
                    await context.Request.InputStream.CopyToAsync(body);
                    if (context.Request.Url!.AbsolutePath == "/v1/traces")
                    {
                        lock (_received)
                        {
                            _received.Add(body.ToArray());
                        }
                    }

                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/x-protobuf";
                    context.Response.Close();
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    return;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _listener.Close();
            }

            base.Dispose(disposing);
        }
    }

    [ClassDataSource<CollectedFactory>]
    public required CollectedFactory Collector { get; init; }

    private static object BookingWithPeopleInIt(Guid facilityId, DateTimeOffset start) =>
        new
        {
            conduct = "Lesson",
            description = Description,
            pocName = PocName,
            pocPhone = PocPhone,
            // More than one slot, which is inserted as a batch, and that has the values in the SQL itself
            slots = new[]
            {
                new { facilityId, startDateTime = start, endDateTime = start.AddHours(1) },
                new { facilityId, startDateTime = start.AddHours(2), endDateTime = start.AddHours(3) },
            },
        };

    /// <summary>The ids of the bookings that were made, which are values in the SQL of the batch that made them.</summary>
    private static async Task<List<string>> BookingIdsAsync(HttpResponseMessage made)
    {
        var ids = (await made.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(b => b.GetProperty("id").GetGuid().ToString()).ToList();
        await Assert.That(ids).HasCount(2);
        return ids;
    }

    private static DateTimeOffset Tomorrow() => DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(1);

    [Test]
    public async Task A_request_that_reads_the_database_has_what_the_statements_did_in_its_trace()
    {
        using var client = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        (await client.GetAsync("/Me")).EnsureSuccessStatusCode();

        var request = await Factory.RequestSpanAsync("/Me");
        var statements = Factory.Spans.ToList().Where(s => s.Source.Name == "MySqlConnector" && s.TraceId == request.TraceId).ToList();
        await Assert.That(statements).IsNotEmpty();
        await Assert.That(statements.Any(s => s.GetTagItem("db.operation") as string == "select")).IsTrue();
        await Assert.That(statements.Any(s => s.GetTagItem("db.operation") as string == "insert")).IsTrue();
    }

    [Test]
    public async Task What_a_batch_has_in_its_statement_is_in_no_span()
    {
        var org = await Factory.CreateOrgAsync();
        var facility = org.AddFacility("Hall");

        var made = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", BookingWithPeopleInIt(facility, Tomorrow()));
        await Assert.That(made).HasStatus(HttpStatusCode.Created);
        var ids = await BookingIdsAsync(made);

        var request = await Factory.RequestSpanAsync("t/{slug}/Bookings");
        var statements = Factory.Spans.ToList().Where(s => s.Source.Name == "MySqlConnector" && s.TraceId == request.TraceId).ToList();

        // The batch did run, as an insert, and said so
        await Assert.That(statements.Any(s => s.GetTagItem("db.operation") as string == "insert")).IsTrue();
        // But no span has any statement in its tags, nor the ids the batch put in it, which it does as values of its own SQL
        var everything = string.Join('\n', Factory.Spans.ToList().SelectMany(s => s.TagObjects).Select(t => $"{t.Key}={t.Value}"));
        await Assert.That(everything.Contains("db.statement") || everything.Contains("db.query.text")).IsFalse();
        foreach (var id in ids)
        {
            await Assert.That(everything.Contains(id)).IsFalse();
        }

        await Assert.That(everything.Contains(PocName)).IsFalse();
        await Assert.That(everything.Contains("91230000")).IsFalse();
        await Assert.That(everything.Contains(Description)).IsFalse();
    }

    [Test]
    public async Task What_is_sent_to_a_collector_has_the_database_in_it_and_no_statements()
    {
        var org = await Collector.CreateOrgAsync();
        var facility = org.AddFacility("Hall");

        var made = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", BookingWithPeopleInIt(facility, Tomorrow()));
        await Assert.That(made).HasStatus(HttpStatusCode.Created);
        var ids = await BookingIdsAsync(made);

        // Spans are sent in batches, every so often
        string sent = string.Empty;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Collector.Flush();
            sent = string.Concat(Collector.Traces.Select(body => Encoding.Latin1.GetString(body)));
            if (sent.Contains("db.operation"))
            {
                break;
            }

            await Task.Delay(100);
        }

        await Assert.That(sent).Contains("MySqlConnector");
        await Assert.That(sent).Contains("db.operation");
        await Assert.That(sent.Contains("db.statement") || sent.Contains("db.query.text")).IsFalse();
        foreach (var id in ids)
        {
            await Assert.That(sent.Contains(id)).IsFalse();
        }

        await Assert.That(sent.Contains(PocName)).IsFalse();
        await Assert.That(sent.Contains("91230000")).IsFalse();
        await Assert.That(sent.Contains(Description)).IsFalse();
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
