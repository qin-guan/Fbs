using Fbs.WebApi.Data;

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
}
