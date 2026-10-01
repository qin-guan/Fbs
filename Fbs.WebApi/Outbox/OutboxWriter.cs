using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using SqlSugar;

namespace Fbs.WebApi.Outbox;

public static class OutboxWriter
{
    /// <summary>
    /// Adds a message to be handled once the transaction it is called in commits. Call
    /// <see cref="OutboxSignal.Notify"/> after that so it is handled straight away, or the dispatcher
    /// finds it on its next poll.
    /// </summary>
    /// <returns>The message's ID.</returns>
    public static async Task<Guid> EnqueueAsync(
        ISqlSugarClient sql,
        Guid tenantId,
        string type,
        object payload,
        CancellationToken cancellationToken = default
    )
    {
        var now = DateTimeOffset.UtcNow;
        // The database keeps whole seconds, and rounds. Left as it is, a message written just before the
        // half second would be dated a moment from now, and not be due when it is looked for
        var due = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var id = Guid.NewGuid();
        await sql.Insertable(
                new OutboxMessage
                {
                    Id = id,
                    TenantId = tenantId,
                    Type = type,
                    Payload = JsonSerializer.Serialize(payload),
                    NextAttemptAt = due,
                    CreatedAt = now,
                }
            )
            .ExecuteCommandAsync(cancellationToken);
        return id;
    }
}
