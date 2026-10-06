namespace Fbs.WebApi.Data;

/// <summary>
/// Runs database work where it cannot share a connection with anything else.
/// </summary>
/// <remarks>
/// <see cref="SqlSugar.SqlSugarScope"/> keeps one client per async context. Hosted services are started on the
/// host's context, and the first use of the client — schema validation, or a service that queries before it
/// awaits — publishes that client to every service started after it. The outbox, the calendar check and the
/// gauges then open one <c>MySqlConnection</c> together, which throws "Cannot Open when State is Connecting".
/// A request does not: it has a context of its own, so long as nothing has published a client onto the host's.
/// </remarks>
public static class SqlSugarContext
{
    /// <summary>Runs <paramref name="work"/> on a fresh execution context, so it gets its own client.</summary>
    public static Task RunIsolatedAsync(Func<Task> work)
    {
        // Disposed on this thread, once the work has been queued. Awaiting inside the using resumes on another
        // thread, and undoing the suppression there throws.
        Task task;
        using (ExecutionContext.SuppressFlow())
        {
            task = Task.Run(work);
        }

        return task;
    }

    /// <summary>As <see cref="RunIsolatedAsync(Func{Task})"/>, for work that returns a value.</summary>
    public static Task<T> RunIsolatedAsync<T>(Func<T> work)
    {
        Task<T> task;
        using (ExecutionContext.SuppressFlow())
        {
            task = Task.Run(work);
        }

        return task;
    }
}
