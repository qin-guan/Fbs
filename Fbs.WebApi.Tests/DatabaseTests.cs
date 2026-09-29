using Fbs.WebApi.Tests.Data;
using SqlSugar;

namespace Fbs.WebApi.Tests;

/// <summary>
/// What the API relies on from SqlSugar and TiDB, whatever tables it goes on to use.
/// </summary>
public class DatabaseTests
{
    private static readonly DateTimeOffset Singapore9Am = new(2026, 3, 7, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Utc1Am = new(2026, 3, 7, 1, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Times_are_stored_as_utc_whatever_offset_they_come_with()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.CreateClient();
        db.CodeFirst.InitTables<Stamp>();

        await db.Insertable(new Stamp { Id = 1, At = Singapore9Am }).ExecuteCommandAsync();

        // The database holds the UTC wall clock time...
        await Assert.That(await db.Ado.GetStringAsync("SELECT CAST(At AS CHAR) FROM Stamp WHERE Id = 1"))
            .IsEqualTo("2026-03-07 01:00:00");
        // ...and it comes back as the same instant, in UTC
        var stamp = await db.Queryable<Stamp>().FirstAsync(s => s.Id == 1);
        await Assert.That(stamp.At).IsEqualTo(Utc1Am);
        await Assert.That(stamp.At.Offset).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Times_in_queries_are_compared_as_the_same_instant_in_any_offset()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.CreateClient();
        db.CodeFirst.InitTables<Stamp>();
        await db.Insertable(new Stamp { Id = 1, At = Utc1Am }).ExecuteCommandAsync();

        // SqlSugar reads captured values from locals, but not from private fields
        var singapore9Am = Singapore9Am;
        var found = await db.Queryable<Stamp>().Where(s => s.At == singapore9Am).CountAsync();

        await Assert.That(found).IsEqualTo(1);
    }

    [Test]
    public async Task A_transaction_that_is_not_committed_is_rolled_back()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.CreateClient();
        db.CodeFirst.InitTables<Stamp>();

        using (db.Ado.UseTran())
        {
            await db.Insertable(new Stamp { Id = 1, At = Utc1Am }).ExecuteCommandAsync();
            // Leaving without committing
        }

        await Assert.That(await db.Queryable<Stamp>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Transactions_default_to_repeatable_read_and_are_pessimistic()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.CreateClient();

        // The booking rules depend on both, see docs/adr/spikes/s3-tidb-concurrency
        await Assert.That(await db.Ado.GetStringAsync("SELECT @@transaction_isolation")).IsEqualTo("REPEATABLE-READ");
        await Assert.That(await db.Ado.GetStringAsync("SELECT @@tidb_txn_mode")).IsEqualTo("pessimistic");
    }

    [SugarTable("Stamp")]
    private class Stamp
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int Id { get; set; }

        public DateTimeOffset At { get; set; }
    }
}
