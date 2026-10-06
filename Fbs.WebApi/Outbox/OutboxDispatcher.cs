using System.Diagnostics;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Telemetry;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Fbs.WebApi.Outbox;

public sealed class OutboxOptions
{
    /// <summary>How long to wait for something new before looking anyway, for messages written by another instance.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a message is held while it is handled. A handler that takes longer than this may have it done twice.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How many messages are taken at a time.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>How many times a message is tried before it is given up on.</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>How long messages that are done are kept.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Only handle this tenant's messages. Unset, it handles everyone's.</summary>
    public Guid? TenantId { get; set; }
}

/// <summary>
/// Does what the outbox says needs doing: takes messages that are due, has the handler for their type do
/// them, and puts back the ones that fail, later each time, until they are given up on.
/// </summary>
/// <remarks>
/// Any number of instances can run at once, as when a new version starts while the old one is stopping.
/// They take messages with a lease (see <see cref="OutboxMessage"/>), so a message is handled by one at a
/// time, and a lease that has run out, because its holder died or was too slow, lets another take over.
/// If the slow one then finishes, it is too late to say anything about the message.
/// </remarks>
public sealed class OutboxDispatcher(
    ILogger<OutboxDispatcher> logger,
    ISqlSugarClient sql,
    IServiceScopeFactory scopeFactory,
    OutboxSignal signal,
    IOptions<OutboxOptions> options
) : BackgroundService
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    // Its own client. On the host's, claiming a message opens the connection the gauges are already opening.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        SqlSugarContext.RunIsolatedAsync(() => DispatchAsync(stoppingToken));

    private async Task DispatchAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessDueAsync(stoppingToken) == 0)
                {
                    await signal.WaitAsync(options.Value.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // The database being unavailable, say: try again shortly instead of giving up for good
                logger.LogError(e, "The outbox dispatcher failed, retrying shortly");
                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>Handles every message that is due, and gives up on those that have used up their attempts.</summary>
    /// <returns>How many messages were handled, whether or not they worked.</returns>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        await GiveUpOnExhaustedAsync(cancellationToken);
        await SkipForInactiveTenantsAsync(cancellationToken);
        await PurgeAsync(cancellationToken);

        var handled = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var token = Guid.NewGuid();
            var claimed = await ClaimAsync(token, cancellationToken);
            if (claimed.Count == 0)
            {
                break;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetServices<IOutboxHandler>().ToDictionary(h => h.Type);
            foreach (var message in claimed)
            {
                await HandleAsync(message, token, handlers, cancellationToken);
                handled++;
            }
        }

        return handled;
    }

    /// <summary>
    /// Leases the messages that are due to whoever asks first. It is one statement, so two dispatchers
    /// asking together get different messages: whichever is second waits for the first and then looks again.
    /// </summary>
    private async Task<List<OutboxMessage>> ClaimAsync(Guid token, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = options.Value.TenantId;
        var parameters = new List<SugarParameter>
        {
            new("@token", token),
            new("@until", now + options.Value.LeaseDuration),
            new("@pending", (int)OutboxStatus.Pending),
            new("@now", now),
            new("@maxAttempts", options.Value.MaxAttempts),
            new("@limit", options.Value.BatchSize),
            new("@active", (int)TenantStatus.Active),
        };
        if (tenantId is { } onlyTenant)
        {
            parameters.Add(new SugarParameter("@tenantId", onlyTenant));
        }

        var claimed = await sql.Ado.ExecuteCommandAsync(
            $"""
            UPDATE OutboxMessage
            SET LockedBy = @token, LockedUntil = @until, Attempts = Attempts + 1
            WHERE Status = @pending
              AND NextAttemptAt <= @now
              AND (LockedUntil IS NULL OR LockedUntil < @now)
              AND Attempts < @maxAttempts
              AND NOT EXISTS (SELECT 1 FROM Tenant WHERE Tenant.Id = OutboxMessage.TenantId AND Tenant.Status <> @active)
              {(tenantId is null ? "" : "AND TenantId = @tenantId")}
            ORDER BY NextAttemptAt, CreatedAt
            LIMIT @limit
            """,
            parameters
        );

        if (claimed == 0)
        {
            return [];
        }

        return await sql.Queryable<OutboxMessage>()
            .Where(m => m.LockedBy == token && m.Status == OutboxStatus.Pending)
            .OrderBy(m => m.NextAttemptAt)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task HandleAsync(
        OutboxMessage message,
        Guid token,
        Dictionary<string, IOutboxHandler> handlers,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!handlers.TryGetValue(message.Type, out var handler))
            {
                throw new OutboxPermanentFailureException($"Nothing handles messages of type '{message.Type}'.");
            }

            var started = Stopwatch.GetTimestamp();
            try
            {
                await handler.HandleAsync(message, cancellationToken);
            }
            finally
            {
                FbsMetrics.OutboxHandleDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("type", message.Type));
            }

            await CompleteAsync(message, token, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping. The lease will run out and it will be tried again
            throw;
        }
        catch (Exception e)
        {
            await FailAsync(message, token, e, cancellationToken);
        }
    }

    private async Task CompleteAsync(OutboxMessage message, Guid token, CancellationToken cancellationToken)
    {
        var id = message.Id;
        var now = DateTimeOffset.UtcNow;
        var updated = await sql.Updateable<OutboxMessage>()
            .SetColumns(m => new OutboxMessage
            {
                Status = OutboxStatus.Done,
                CompletedAt = now,
                LockedBy = null,
                LockedUntil = null,
                LastError = null,
            })
            .Where(m => m.Id == id && m.LockedBy == token)
            .ExecuteCommandAsync(cancellationToken);

        if (updated == 0)
        {
            logger.LogWarning(
                "Outbox message {Id} ({Type}) was handled after its lease ran out, and has been taken by another dispatcher",
                message.Id,
                message.Type
            );
        }
        else
        {
            Count(message.Type, "done", 1);
        }
    }

    /// <summary>What came of a message, by its type. Handled twice, because its lease ran out, is not counted the second time.</summary>
    private static void Count(string type, string outcome, long messages) =>
        FbsMetrics.OutboxMessages.Add(messages, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("outcome", outcome));

    private async Task FailAsync(OutboxMessage message, Guid token, Exception exception, CancellationToken cancellationToken)
    {
        var id = message.Id;
        var now = DateTimeOffset.UtcNow;
        var error = Truncate($"{exception.GetType().Name}: {exception.Message}", 1000);
        var giveUp = exception is OutboxPermanentFailureException || message.Attempts >= options.Value.MaxAttempts;
        var nextAttemptAt = now + Backoff(message.Attempts);

        var updated = giveUp
            ? await sql.Updateable<OutboxMessage>()
                .SetColumns(m => new OutboxMessage
                {
                    Status = OutboxStatus.Dead,
                    LockedBy = null,
                    LockedUntil = null,
                    LastError = error,
                })
                .Where(m => m.Id == id && m.LockedBy == token)
                .ExecuteCommandAsync(cancellationToken)
            : await sql.Updateable<OutboxMessage>()
                .SetColumns(m => new OutboxMessage
                {
                    NextAttemptAt = nextAttemptAt,
                    LockedBy = null,
                    LockedUntil = null,
                    LastError = error,
                })
                .Where(m => m.Id == id && m.LockedBy == token)
                .ExecuteCommandAsync(cancellationToken);

        if (updated == 0)
        {
            logger.LogWarning(
                exception,
                "Outbox message {Id} ({Type}) failed after its lease ran out, and has been taken by another dispatcher",
                message.Id,
                message.Type
            );
        }
        else if (giveUp)
        {
            Count(message.Type, "dead", 1);
            logger.LogError(
                exception,
                "Gave up on outbox message {Id} ({Type}) after {Attempts} attempts",
                message.Id,
                message.Type,
                message.Attempts
            );
        }
        else
        {
            Count(message.Type, "retry", 1);
            logger.LogWarning(
                exception,
                "Outbox message {Id} ({Type}) failed on attempt {Attempts}, trying again later",
                message.Id,
                message.Type,
                message.Attempts
            );
        }
    }

    /// <summary>
    /// A message that used up its attempts because its dispatcher kept dying, so nothing was left to say it
    /// failed. Without this it would wait for ever.
    /// </summary>
    private async Task GiveUpOnExhaustedAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var maxAttempts = options.Value.MaxAttempts;
        var tenantId = options.Value.TenantId;
        var gaveUp = await sql.Updateable<OutboxMessage>()
            .SetColumns(m => new OutboxMessage
            {
                Status = OutboxStatus.Dead,
                LockedBy = null,
                LockedUntil = null,
                LastError = "Gave up after its dispatcher kept stopping before it finished.",
            })
            .Where(m => m.Status == OutboxStatus.Pending && m.Attempts >= maxAttempts && (m.LockedUntil == null || m.LockedUntil < now))
            .WhereIF(tenantId is not null, m => m.TenantId == tenantId)
            .ExecuteCommandAsync(cancellationToken);
        if (gaveUp > 0)
        {
            // Not known by type, as it is one statement
            Count("unknown", "dead", gaveUp);
        }
    }

    /// <summary>
    /// Messages of an organisation that can't be used, of a type that is only worth handling at the time, are given up on. The
    /// rest are not claimed while it can't be (see <see cref="ClaimAsync"/>), and wait for it.
    /// </summary>
    private async Task SkipForInactiveTenantsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var types = scope.ServiceProvider.GetServices<IOutboxHandler>().Where(h => h.WhenTenantInactive == OutboxInactiveTenantPolicy.Skip).Select(h => h.Type).Distinct().ToList();
        if (types.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var tenantId = options.Value.TenantId;
        foreach (var type in types)
        {
            var parameters = new List<SugarParameter>
            {
                new("@skipped", (int)OutboxStatus.Skipped),
                new("@pending", (int)OutboxStatus.Pending),
                new("@active", (int)TenantStatus.Active),
                new("@now", now),
                new("@reason", "Skipped: the organisation could not be used, so nobody was told."),
                new("@type", type),
            };
            if (tenantId is { } onlyTenant)
            {
                parameters.Add(new SugarParameter("@tenantId", onlyTenant));
            }

            var skipped = await sql.Ado.ExecuteCommandAsync(
                $"""
                UPDATE OutboxMessage
                SET Status = @skipped, CompletedAt = @now, LockedBy = NULL, LockedUntil = NULL, LastError = @reason
                WHERE Status = @pending
                  AND (LockedUntil IS NULL OR LockedUntil < @now)
                  AND Type = @type
                  AND EXISTS (SELECT 1 FROM Tenant WHERE Tenant.Id = OutboxMessage.TenantId AND Tenant.Status <> @active)
                  {(tenantId is null ? "" : "AND TenantId = @tenantId")}
                """,
                parameters
            );
            if (skipped > 0)
            {
                Count(type, "skipped", skipped);
            }
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPurge < PurgeInterval)
        {
            return;
        }

        _lastPurge = now;
        var before = now - options.Value.Retention;
        var tenantId = options.Value.TenantId;
        await sql.Deleteable<OutboxMessage>()
            .Where(m => (m.Status == OutboxStatus.Done || m.Status == OutboxStatus.Skipped) && m.CompletedAt < before)
            .WhereIF(tenantId is not null, m => m.TenantId == tenantId)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>5 seconds after the first failure, then twice as long each time, up to 15 minutes.</summary>
    internal static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(15 * 60, 5 * Math.Pow(2, Math.Max(0, attempts - 1))));

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
