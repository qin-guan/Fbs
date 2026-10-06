using Fbs.WebApi.Data;
using SqlSugar;

namespace Fbs.WebApi.Tests;

/// <summary>
/// The production failure: hosted services inherit the host's SqlSugar client and open its connection together.
/// </summary>
public class SqlSugarContextTests
{
    [Test]
    public async Task Work_started_on_this_context_shares_its_client_unless_it_is_isolated()
    {
        var scope = SqlSugarClientFactory.Create("Server=127.0.0.1;Port=1;Database=none;User=none;Password=none");
        var startup = scope.ScopedContext;

        // What BackgroundService.StartAsync does: run the loop as a child of the host's context
        var inherited = await Task.Run(async () => scope.ScopedContext);
        await Assert.That(ReferenceEquals(startup, inherited)).IsTrue();

        SqlSugarClient? gauges = null;
        SqlSugarClient? outbox = null;
        await SqlSugarContext.RunIsolatedAsync(async () =>
        {
            gauges = scope.ScopedContext;
            await Task.Yield();
            await Assert.That(ReferenceEquals(gauges, scope.ScopedContext)).IsTrue();
        });
        await SqlSugarContext.RunIsolatedAsync(() =>
        {
            outbox = scope.ScopedContext;
            return Task.CompletedTask;
        });

        await Assert.That(ReferenceEquals(startup, gauges)).IsFalse();
        await Assert.That(ReferenceEquals(startup, outbox)).IsFalse();
        await Assert.That(ReferenceEquals(gauges, outbox)).IsFalse();
    }
}
