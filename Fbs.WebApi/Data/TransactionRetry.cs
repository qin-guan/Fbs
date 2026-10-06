using Fbs.WebApi.Telemetry;

namespace Fbs.WebApi.Data;

/// <summary>
/// Runs a transaction again when the database gave up on it for a reason that another go will get past.
/// </summary>
/// <remarks>
/// With pessimistic locking, a transaction can be rejected when many others want the same rows. TiDB
/// stops trying to lock them after a number of attempts ("pessimistic lock retry limit reached"). A deadlock or a
/// wait that runs too long is ended by the database. The transaction is rolled back, so none of these has changed
/// anything. The work has to be the whole transaction, from taking the locks, and it must not do anything outside
/// the database until the transaction has committed.
/// </remarks>
public static class TransactionRetry
{
    public const int Attempts = 5;

    private static readonly string[] Transient =
    [
        "pessimistic lock retry limit reached",
        "Deadlock found",
        "Lock wait timeout exceeded",
        "Write conflict",
    ];

    public static bool IsTransient(Exception exception) => ReasonOf(exception) is not null;

    /// <summary>Why the database turned it away, as something to tag by, or null if it isn't one of those.</summary>
    private static string? ReasonOf(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            for (var i = 0; i < Transient.Length; i++)
            {
                if (e.Message.Contains(Transient[i], StringComparison.OrdinalIgnoreCase))
                {
                    return Reasons[i];
                }
            }
        }

        return null;
    }

    private static readonly string[] Reasons = ["lock_retry_limit", "deadlock", "lock_wait_timeout", "write_conflict"];

    /// <summary>Runs <paramref name="attempt"/>, and again after a short random wait if it fails for one of those reasons, up to <see cref="Attempts"/> times.</summary>
    public static async Task<T> RunAsync<T>(Func<Task<T>> attempt, CancellationToken cancellationToken = default)
    {
        for (var n = 1; ; n++)
        {
            try
            {
                return await attempt();
            }
            catch (Exception e) when (n >= Attempts && IsTransient(e))
            {
                // The retries are used up, so the caller gets the error.
                FbsMetrics.TransactionsGivenUp.Add(1, new KeyValuePair<string, object?>("reason", ReasonOf(e)));
                throw;
            }
            catch (Exception e) when (n < Attempts && IsTransient(e) && !cancellationToken.IsCancellationRequested)
            {
                FbsMetrics.TransactionRetries.Add(1, new KeyValuePair<string, object?>("reason", ReasonOf(e)));
                // Longer each time, and different for each caller, so those that collided don't do so again together
                await Task.Delay(Random.Shared.Next(5, 25 * n), cancellationToken);
            }
        }
    }

    public static async Task RunAsync(Func<Task> attempt, CancellationToken cancellationToken = default) =>
        await RunAsync(
            async () =>
            {
                await attempt();
                return true;
            },
            cancellationToken
        );
}
