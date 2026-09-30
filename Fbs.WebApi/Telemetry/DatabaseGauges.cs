using System.Diagnostics.Metrics;
using Fbs.WebApi.Data.Entities;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Fbs.WebApi.Telemetry;

/// <summary>What is in the database that is worth watching, as of the last time it was looked at.</summary>
public sealed record DatabaseSnapshot(
    IReadOnlyDictionary<string, long> OutboxDueByType,
    IReadOnlyDictionary<string, double> OutboxOldestDueSeconds,
    IReadOnlyDictionary<string, long> OutboxDeadByType,
    long OutboxHeld,
    IReadOnlyDictionary<string, long> Tenants,
    IReadOnlyDictionary<string, long> Members,
    IReadOnlyDictionary<string, long> Calendars
)
{
    public static readonly DatabaseSnapshot Empty = new(
        new Dictionary<string, long>(),
        new Dictionary<string, double>(),
        new Dictionary<string, long>(),
        0,
        new Dictionary<string, long>(),
        new Dictionary<string, long>(),
        new Dictionary<string, long>()
    );
}

/// <summary>
/// The gauges: how much is waiting to be sent and for how long, how many messages have been given up on, and how many
/// organisations, people and calendars there are in each state. They are read from the database now and then rather than
/// when they are asked for, so being scraped is never a query, and are the same whichever instance is asked, so with more than one,
/// take the largest of them and not the sum.
/// </summary>
public static class DatabaseGauges
{
    private static volatile DatabaseSnapshot _current = DatabaseSnapshot.Empty;

    /// <summary>The latest look. Nothing is reported until there has been one.</summary>
    public static DatabaseSnapshot Current => _current;

    static DatabaseGauges()
    {
        var meter = FbsMetrics.Meter;
        meter.CreateObservableGauge<long>("fbs.outbox.pending", () => Measure(_current.OutboxDueByType, "type"), "{message}", "Messages due to be handled and not handled yet, by type");
        meter.CreateObservableGauge(
            "fbs.outbox.oldest_due.age",
            () => _current.OutboxOldestDueSeconds.Select(x => new Measurement<double>(x.Value, new KeyValuePair<string, object?>("type", x.Key))),
            "s",
            "How long ago the oldest message that is due should have been handled, by type. Growing means what is sent is not getting there"
        );
        meter.CreateObservableGauge("fbs.outbox.dead", () => Measure(_current.OutboxDeadByType, "type"), "{message}", "Messages given up on and kept to see what failed, by type. Anything but 0 needs looking at");
        meter.CreateObservableGauge("fbs.outbox.held", () => _current.OutboxHeld, "{message}", "Messages waiting because their organisation is suspended, or is to be deleted");
        meter.CreateObservableGauge("fbs.tenants", () => Measure(_current.Tenants, "status"), "{tenant}", "Organisations, by status");
        meter.CreateObservableGauge("fbs.members", () => Measure(_current.Members, "status"), "{member}", "People in organisations, by status");
        meter.CreateObservableGauge("fbs.calendar.connections", () => Measure(_current.Calendars, "status"), "{connection}", "Calendars connected, by status. Failed ones are not sent to until put right");
    }

    private static IEnumerable<Measurement<long>> Measure(IReadOnlyDictionary<string, long> values, string tag) =>
        values.Select(x => new Measurement<long>(x.Value, new KeyValuePair<string, object?>(tag, x.Key)));

    /// <summary>Looks in the database, and keeps what it found for the gauges.</summary>
    public static async Task RefreshAsync(ISqlSugarClient sql, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = OutboxStatus.Pending;
        var dead = OutboxStatus.Dead;
        var active = TenantStatus.Active;

        // What is due and is not held by a suspended organisation, which is what is late if it is old
        var due = await sql.Queryable<OutboxMessage>()
            .Where(m => m.Status == pending && m.NextAttemptAt <= now && !SqlFunc.Subqueryable<Tenant>().Where(t => t.Id == m.TenantId && t.Status != active).Any())
            .GroupBy(m => m.Type)
            .Select(m => new TypeCount { Type = m.Type, Count = SqlFunc.AggregateCount(m.Id), Oldest = SqlFunc.AggregateMin(m.NextAttemptAt) })
            .ToListAsync(cancellationToken);
        var held = await sql.Queryable<OutboxMessage>()
            .Where(m => m.Status == pending && SqlFunc.Subqueryable<Tenant>().Where(t => t.Id == m.TenantId && t.Status != active).Any())
            .CountAsync(cancellationToken);
        var deadByType = await sql.Queryable<OutboxMessage>()
            .Where(m => m.Status == dead)
            .GroupBy(m => m.Type)
            .Select(m => new TypeCount { Type = m.Type, Count = SqlFunc.AggregateCount(m.Id) })
            .ToListAsync(cancellationToken);
        var tenants = await sql.Queryable<Tenant>().GroupBy(t => t.Status).Select(t => new StatusCount { Status = (int)t.Status, Count = SqlFunc.AggregateCount(t.Id) }).ToListAsync(cancellationToken);
        var members = await sql.Queryable<TenantMember>().GroupBy(m => m.Status).Select(m => new StatusCount { Status = (int)m.Status, Count = SqlFunc.AggregateCount(m.Id) }).ToListAsync(cancellationToken);
        var calendars = await sql.Queryable<CalendarConnection>().GroupBy(c => c.Status).Select(c => new StatusCount { Status = (int)c.Status, Count = SqlFunc.AggregateCount(c.Id) }).ToListAsync(cancellationToken);

        // Every state is reported, including when there is none, so that a line doesn't disappear when it goes to 0
        _current = new DatabaseSnapshot(
            due.ToDictionary(d => d.Type, d => (long)d.Count),
            due.ToDictionary(d => d.Type, d => Math.Max(0, (now - d.Oldest).TotalSeconds)),
            deadByType.ToDictionary(d => d.Type, d => (long)d.Count),
            held,
            Every<TenantStatus>(tenants),
            Every<MemberStatus>(members),
            Every<CalendarConnectionStatus>(calendars)
        );
    }

    private static Dictionary<string, long> Every<T>(List<StatusCount> counts)
        where T : struct, Enum =>
        Enum.GetValues<T>().ToDictionary(status => status.ToString(), status => counts.Where(c => c.Status == Convert.ToInt32(status)).Sum(c => (long)c.Count));

    private sealed class TypeCount
    {
        public string Type { get; set; } = string.Empty;

        public int Count { get; set; }

        public DateTimeOffset Oldest { get; set; }
    }

    private sealed class StatusCount
    {
        public int Status { get; set; }

        public int Count { get; set; }
    }
}

public sealed class DatabaseGaugesOptions
{
    /// <summary>How often the database is looked at. Zero or less turns it off, which is what the tests do so that they don't overwrite each other's.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Keeps <see cref="DatabaseGauges"/> up to date.</summary>
public sealed class DatabaseGaugesService(ILogger<DatabaseGaugesService> logger, ISqlSugarClient sql, IOptions<DatabaseGaugesOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await DatabaseGauges.RefreshAsync(sql, stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // The database being away is what the health check is for: the last look stays, and is looked for again
                logger.LogWarning(e, "Looking at the database for the gauges failed, and will be tried again");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
