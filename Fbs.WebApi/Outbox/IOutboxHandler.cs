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

    /// <summary>
    /// What is done with a message of an organisation that isn't active, which is one that has been suspended, or is to be deleted:
    /// nothing is sent for it, to anywhere. By default it waits, and is handled if the organisation is made active again, which is
    /// right for what puts something in step (a calendar). Something that is only worth doing at the time, such as telling people,
    /// is skipped.
    /// </summary>
    OutboxInactiveTenantPolicy WhenTenantInactive => OutboxInactiveTenantPolicy.Hold;
}

public enum OutboxInactiveTenantPolicy
{
    /// <summary>It waits, without being tried, until the organisation is active.</summary>
    Hold = 0,

    /// <summary>It is given up on, without being tried, and marked as skipped.</summary>
    Skip = 1,
}

/// <summary>Thrown by a handler when trying again can't help, such as a message that makes no sense.</summary>
public sealed class OutboxPermanentFailureException(string message, Exception? innerException = null)
    : Exception(message, innerException);
