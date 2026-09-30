using Fbs.WebApi.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

/// <summary>Trying a transaction again when the database turned it away, and only then.</summary>
public class TransactionRetryTests
{
    private static Exception Turned(string message = "pessimistic lock retry limit reached") => new InvalidOperationException("wrapper", new Exception(message));

    [Test]
    [Arguments("pessimistic lock retry limit reached")]
    [Arguments("Deadlock found when trying to get lock; try restarting transaction")]
    [Arguments("Lock wait timeout exceeded; try restarting transaction")]
    [Arguments("Write conflict, txnStartTS=1")]
    public async Task What_the_database_turned_away_is_tried_again_until_it_goes_through(string reason)
    {
        var calls = 0;

        var result = await TransactionRetry.RunAsync(() => ++calls < 3 ? throw Turned(reason) : Task.FromResult("done"));

        await Assert.That(result).IsEqualTo("done");
        await Assert.That(calls).IsEqualTo(3);
    }

    [Test]
    public async Task Anything_else_fails_at_once()
    {
        var calls = 0;

        var thrown = await Assert.That(() => TransactionRetry.RunAsync<int>(() => { calls++; throw new KeyNotFoundException("Booking does not exist."); })).Throws<KeyNotFoundException>();

        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(thrown!.Message).Contains("does not exist");
    }

    [Test]
    public async Task It_gives_up_after_a_few_goes_with_what_the_database_said()
    {
        var calls = 0;

        var thrown = await Assert.That(() => TransactionRetry.RunAsync<int>(() => { calls++; throw Turned(); })).Throws<InvalidOperationException>();

        await Assert.That(calls).IsEqualTo(TransactionRetry.Attempts);
        await Assert.That(thrown!.InnerException!.Message).Contains("retry limit");
    }

    [Test]
    public async Task It_stops_trying_when_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        var calls = 0;

        var thrown = await Assert.That(() => TransactionRetry.RunAsync<int>(() => { calls++; cancel.Cancel(); throw Turned(); }, cancel.Token)).Throws<InvalidOperationException>();

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    [Arguments("pessimistic lock retry limit reached", "lock_retry_limit")]
    [Arguments("Deadlock found when trying to get lock; try restarting transaction", "deadlock")]
    [Arguments("Lock wait timeout exceeded; try restarting transaction", "lock_wait_timeout")]
    [Arguments("Write conflict, txnStartTS=1", "write_conflict")]
    public async Task Each_go_again_is_counted_by_why_the_database_turned_it_away(string reason, string tag)
    {
        var calls = 0;
        using var metrics = new MetricsRecorder();

        await TransactionRetry.RunAsync(() => ++calls < 3 ? throw Turned(reason) : Task.FromResult(1));

        await Assert.That(metrics.Sum("fbs.db.transaction.retries", ("reason", tag))).IsEqualTo(2);
        await Assert.That(metrics.Sum("fbs.db.transaction.retries")).IsEqualTo(2);
        await Assert.That(metrics.Sum("fbs.db.transaction.given_up")).IsEqualTo(0);
    }

    [Test]
    public async Task One_that_is_given_up_on_is_counted_as_somebody_who_got_an_error_and_not_as_one_more_go_again()
    {
        using var metrics = new MetricsRecorder();

        await Assert.That(() => TransactionRetry.RunAsync<int>(() => throw Turned("Deadlock found"))).Throws<InvalidOperationException>();

        await Assert.That(metrics.Sum("fbs.db.transaction.given_up", ("reason", "deadlock"))).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.db.transaction.retries", ("reason", "deadlock"))).IsEqualTo(TransactionRetry.Attempts - 1);
    }

    [Test]
    public async Task What_fails_for_any_other_reason_and_what_goes_through_first_time_are_not_counted()
    {
        using var metrics = new MetricsRecorder();

        await TransactionRetry.RunAsync(() => Task.FromResult(1));
        await Assert.That(() => TransactionRetry.RunAsync<int>(() => throw new KeyNotFoundException("Booking does not exist."))).Throws<KeyNotFoundException>();

        await Assert.That(metrics.Sum("fbs.db.transaction.retries")).IsEqualTo(0);
        await Assert.That(metrics.Sum("fbs.db.transaction.given_up")).IsEqualTo(0);
    }
}
