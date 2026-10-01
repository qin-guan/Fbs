using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Outbox;

/// <summary>Does the work for one type of outbox message.</summary>
public interface IOutboxHandler
{
    /// <summary>The <see cref="OutboxMessage.Type"/> it handles.</summary>
    string Type { get; }

    /// <summary>
    /// Does it. It can be called more than once for the same message, when it failed part way or its
    /// dispatcher died, so it has to be safe to repeat, or to pick up where it left off. Throwing means
    /// try again later.
    /// </summary>
    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>Thrown by a handler when trying again can't help, such as a message that makes no sense.</summary>
public sealed class OutboxPermanentFailureException(string message, Exception? innerException = null)
    : Exception(message, innerException);
