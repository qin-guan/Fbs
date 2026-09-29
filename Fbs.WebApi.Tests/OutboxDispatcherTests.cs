using System.Collections.Concurrent;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Tests.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Fbs.WebApi.Tests;

/// <summary>
/// The outbox against TiDB: that a message is handled once, however many dispatchers there are, that
/// failures are tried again later and eventually given up on, and that one that runs out of time can't
/// undo the one that took over.
/// </summary>
public class OutboxDispatcherTests
{
    private const string Type = "test.message";

    private sealed class Handler : IOutboxHandler
    {
        public string Type => OutboxDispatcherTests.Type;

        public ConcurrentQueue<Guid> Handled { get; } = new();

        public Func<OutboxMessage, Task> Action { get; set; } = _ => Task.CompletedTask;

        public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Handled.Enqueue(message.Id);
            await Action(message);
        }
    }

    private sealed class Setup(TestTenant tenant, Handler handler)
    {
        public TestTenant Tenant { get; } = tenant;

        public Handler Handler { get; } = handler;

        public SqlSugarScope Db => Tenant.Db;

        /// <summary>A dispatcher of its own, with a database client of its own, as another instance of the API would have.</summary>
        public async Task<OutboxDispatcher> DispatcherAsync(Handler? handler = null, Action<OutboxOptions>? configure = null)
        {
            var options = new OutboxOptions
            {
                TenantId = Tenant.TenantId,
                MaxAttempts = 3,
                BatchSize = 7,
                PollInterval = TimeSpan.FromMinutes(1),
            };
            configure?.Invoke(options);

            var services = new ServiceCollection();
            services.AddSingleton<IOutboxHandler>(handler ?? Handler);
            return new OutboxDispatcher(
                NullLogger<OutboxDispatcher>.Instance,
                await Tenant.NewClientAsync(),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new OutboxSignal(),
                Microsoft.Extensions.Options.Options.Create(options)
            );
        }

        public Task<Guid> EnqueueAsync(string type = Type, Guid? tenantId = null) =>
            OutboxWriter.EnqueueAsync(Db, tenantId ?? Tenant.TenantId, type, new { Note = "hello" });

        public OutboxMessage Row(Guid id) => Db.Queryable<OutboxMessage>().Single(m => m.Id == id);

        public void MakeDue(Guid id) =>
            Db.Updateable<OutboxMessage>().SetColumns(m => new OutboxMessage { NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1) }).Where(m => m.Id == id).ExecuteCommand();
    }

    private static async Task<Setup> SetUpAsync() => new(await TestTenant.CreateAsync(), new Handler());

    [Test]
    public async Task A_message_is_handled_once_and_marked_done()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync();
        var dispatcher = await setup.DispatcherAsync();

        var handled = await dispatcher.ProcessDueAsync();
        var again = await dispatcher.ProcessDueAsync();

        await Assert.That(handled).IsEqualTo(1);
        await Assert.That(again).IsEqualTo(0);
        await Assert.That(setup.Handler.Handled).IsEquivalentTo([id]);
        var row = setup.Row(id);
        await Assert.That(row.Status).IsEqualTo(OutboxStatus.Done);
        await Assert.That(row.Attempts).IsEqualTo(1);
        await Assert.That(row.CompletedAt).IsNotNull();
        await Assert.That(row.LockedBy).IsNull();
        await Assert.That(row.Payload).Contains("hello");
    }

    [Test]
    public async Task A_message_that_fails_is_tried_again_later_and_each_time_later_than_the_last()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync();
        var dispatcher = await setup.DispatcherAsync();
        var fail = true;
        setup.Handler.Action = _ => fail ? throw new InvalidOperationException("Telegram is down") : Task.CompletedTask;

        var before = DateTimeOffset.UtcNow;
        await dispatcher.ProcessDueAsync();
        var first = setup.Row(id);
        setup.MakeDue(id);
        await dispatcher.ProcessDueAsync();
        var second = setup.Row(id);

        await Assert.That(first.Status).IsEqualTo(OutboxStatus.Pending);
        await Assert.That(first.Attempts).IsEqualTo(1);
        await Assert.That(first.LastError).Contains("Telegram is down");
        await Assert.That(first.NextAttemptAt - before).IsBetween(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(30));
        await Assert.That(second.Attempts).IsEqualTo(2);
        await Assert.That(second.NextAttemptAt - DateTimeOffset.UtcNow).IsGreaterThan(TimeSpan.FromSeconds(8));
        // Not due yet, so not tried again straight away
        await Assert.That(await dispatcher.ProcessDueAsync()).IsEqualTo(0);

        fail = false;
        setup.MakeDue(id);
        await dispatcher.ProcessDueAsync();
        var done = setup.Row(id);
        await Assert.That(done.Status).IsEqualTo(OutboxStatus.Done);
        await Assert.That(done.Attempts).IsEqualTo(3);
        await Assert.That(done.LastError).IsNull();
    }

    [Test]
    public async Task A_message_that_keeps_failing_is_given_up_on_and_kept_to_see_what_failed()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync();
        var dispatcher = await setup.DispatcherAsync();
        setup.Handler.Action = _ => throw new InvalidOperationException("Still down");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            setup.MakeDue(id);
            await dispatcher.ProcessDueAsync();
        }

        setup.MakeDue(id);
        await Assert.That(await dispatcher.ProcessDueAsync()).IsEqualTo(0);
        var row = setup.Row(id);
        await Assert.That(row.Status).IsEqualTo(OutboxStatus.Dead);
        await Assert.That(row.Attempts).IsEqualTo(3);
        await Assert.That(row.LastError).Contains("Still down");
        await Assert.That(setup.Handler.Handled.Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_permanent_failure_is_given_up_on_at_once()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync();
        var dispatcher = await setup.DispatcherAsync();
        setup.Handler.Action = _ => throw new OutboxPermanentFailureException("Makes no sense");

        await dispatcher.ProcessDueAsync();

        var row = setup.Row(id);
        await Assert.That(row.Status).IsEqualTo(OutboxStatus.Dead);
        await Assert.That(row.Attempts).IsEqualTo(1);
        await Assert.That(row.LastError).Contains("Makes no sense");
    }

    [Test]
    public async Task A_message_of_a_type_nothing_handles_is_given_up_on()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync(type: "test.other");
        var dispatcher = await setup.DispatcherAsync();

        await dispatcher.ProcessDueAsync();

        var row = setup.Row(id);
        await Assert.That(row.Status).IsEqualTo(OutboxStatus.Dead);
        await Assert.That(row.LastError).Contains("test.other");
        await Assert.That(setup.Handler.Handled).IsEmpty();
    }

    [Test]
    public async Task Dispatchers_at_the_same_moment_handle_each_message_once()
    {
        var setup = await SetUpAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 60; i++)
        {
            ids.Add(await setup.EnqueueAsync());
        }

        var handlers = Enumerable.Range(0, 4).Select(_ => new Handler()).ToList();
        var dispatchers = new List<OutboxDispatcher>();
        foreach (var handler in handlers)
        {
            dispatchers.Add(await setup.DispatcherAsync(handler));
        }

        List<Task<int>> tasks;
        using (ExecutionContext.SuppressFlow())
        {
            tasks = dispatchers.Select(d => Task.Run(() => d.ProcessDueAsync())).ToList();
        }

        var handled = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));

        var all = handlers.SelectMany(h => h.Handled).ToList();
        await Assert.That(handled.Sum()).IsEqualTo(60);
        await Assert.That(all.Count).IsEqualTo(60);
        await Assert.That(all.Distinct().Count()).IsEqualTo(60);
        await Assert.That(all).IsEquivalentTo(ids);
        await Assert.That(ids.Select(id => setup.Row(id).Status).Distinct()).IsEquivalentTo([OutboxStatus.Done]);
        // Each of them did some, rather than one doing the lot
        await Assert.That(handlers.Count(h => !h.Handled.IsEmpty)).IsGreaterThan(1);
    }

    [Test]
    public async Task Messages_are_handled_in_the_order_they_were_written_even_within_a_second()
    {
        var setup = await SetUpAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 30; i++)
        {
            ids.Add(await setup.EnqueueAsync());
        }

        var dispatcher = await setup.DispatcherAsync();
        await dispatcher.ProcessDueAsync();

        await Assert.That(setup.Handler.Handled.ToList()).IsEquivalentTo(ids, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_message_whose_lease_ran_out_is_taken_over()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync();
        // As if another dispatcher took it and died
        setup.Db.Updateable<OutboxMessage>()
            .SetColumns(m => new OutboxMessage { LockedBy = Guid.NewGuid(), LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1), Attempts = 1 })
            .Where(m => m.Id == id)
            .ExecuteCommand();
        var dispatcher = await setup.DispatcherAsync();

        await dispatcher.ProcessDueAsync();

        var row = setup.Row(id);
        await Assert.That(row.Status).IsEqualTo(OutboxStatus.Done);
        await Assert.That(row.Attempts).IsEqualTo(2);
    }

    [Test]
    public async Task A_message_someone_is_still_handling_is_left_alone()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync();
        setup.Db.Updateable<OutboxMessage>()
            .SetColumns(m => new OutboxMessage { LockedBy = Guid.NewGuid(), LockedUntil = DateTimeOffset.UtcNow.AddMinutes(1), Attempts = 1 })
            .Where(m => m.Id == id)
            .ExecuteCommand();
        var dispatcher = await setup.DispatcherAsync();

        await Assert.That(await dispatcher.ProcessDueAsync()).IsEqualTo(0);

        await Assert.That(setup.Row(id).Status).IsEqualTo(OutboxStatus.Pending);
        await Assert.That(setup.Handler.Handled).IsEmpty();
    }

    [Test]
    public async Task A_message_that_used_up_its_attempts_by_its_dispatcher_dying_is_given_up_on()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync();
        setup.Db.Updateable<OutboxMessage>()
            .SetColumns(m => new OutboxMessage { LockedBy = Guid.NewGuid(), LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1), Attempts = 3 })
            .Where(m => m.Id == id)
            .ExecuteCommand();
        var dispatcher = await setup.DispatcherAsync();

        await Assert.That(await dispatcher.ProcessDueAsync()).IsEqualTo(0);

        var row = setup.Row(id);
        await Assert.That(row.Status).IsEqualTo(OutboxStatus.Dead);
        await Assert.That(row.LastError).Contains("kept stopping");
        await Assert.That(setup.Handler.Handled).IsEmpty();
    }

    [Test]
    public async Task A_dispatcher_that_is_too_slow_cannot_undo_the_one_that_took_over()
    {
        var setup = await SetUpAsync();
        var id = await setup.EnqueueAsync();
        var slow = new Handler();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        slow.Action = async _ =>
        {
            started.SetResult();
            await release.Task;
            throw new InvalidOperationException("Too late to matter");
        };
        var slowDispatcher = await setup.DispatcherAsync(slow);
        var fastDispatcher = await setup.DispatcherAsync(new Handler());

        Task<int> slowRun;
        using (ExecutionContext.SuppressFlow())
        {
            slowRun = Task.Run(() => slowDispatcher.ProcessDueAsync());
        }

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Its lease runs out, and the other one takes it and finishes
        setup.Db.Updateable<OutboxMessage>().SetColumns(m => new OutboxMessage { LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1) }).Where(m => m.Id == id).ExecuteCommand();
        await fastDispatcher.ProcessDueAsync();
        var done = setup.Row(id);

        release.SetResult();
        await slowRun.WaitAsync(TimeSpan.FromSeconds(10));

        var row = setup.Row(id);
        await Assert.That(done.Status).IsEqualTo(OutboxStatus.Done);
        await Assert.That(row.Status).IsEqualTo(OutboxStatus.Done);
        await Assert.That(row.CompletedAt).IsEqualTo(done.CompletedAt);
        await Assert.That(row.LastError).IsNull();
        await Assert.That(row.NextAttemptAt).IsEqualTo(done.NextAttemptAt);
    }

    [Test]
    public async Task Messages_that_are_done_are_kept_for_a_while_and_then_removed()
    {
        var setup = await SetUpAsync();
        var old = await setup.EnqueueAsync();
        var recent = await setup.EnqueueAsync();
        var dispatcher = await setup.DispatcherAsync();
        await dispatcher.ProcessDueAsync();
        setup.Db.Updateable<OutboxMessage>().SetColumns(m => new OutboxMessage { CompletedAt = DateTimeOffset.UtcNow.AddDays(-8) }).Where(m => m.Id == old).ExecuteCommand();
        // The purge only looks now and then, so use a dispatcher that hasn't yet
        var later = await setup.DispatcherAsync();

        await later.ProcessDueAsync();

        await Assert.That(setup.Db.Queryable<OutboxMessage>().Any(m => m.Id == old)).IsFalse();
        await Assert.That(setup.Db.Queryable<OutboxMessage>().Any(m => m.Id == recent)).IsTrue();
    }

    [Test]
    public async Task A_dispatcher_only_handles_the_messages_of_the_tenant_it_is_for()
    {
        var setup = await SetUpAsync();
        var other = await TestTenant.CreateAsync();
        var mine = await setup.EnqueueAsync();
        var theirs = await setup.EnqueueAsync(tenantId: other.TenantId);
        var dispatcher = await setup.DispatcherAsync();

        await dispatcher.ProcessDueAsync();

        await Assert.That(setup.Row(mine).Status).IsEqualTo(OutboxStatus.Done);
        await Assert.That(setup.Row(theirs).Status).IsEqualTo(OutboxStatus.Pending);
        await Assert.That(setup.Handler.Handled).IsEquivalentTo([mine]);
    }

    [Test]
    public async Task A_message_in_a_transaction_that_does_not_commit_is_never_handled()
    {
        var setup = await SetUpAsync();
        var db = await setup.Tenant.NewClientAsync();

        using (var tran = db.Ado.UseTran())
        {
            await OutboxWriter.EnqueueAsync(db, setup.Tenant.TenantId, Type, new { Note = "never" });
            // Not committed
        }

        var tenantId = setup.Tenant.TenantId;
        await Assert.That(setup.Db.Queryable<OutboxMessage>().Count(m => m.TenantId == tenantId)).IsEqualTo(0);
    }

    [Test]
    public async Task Telling_the_dispatcher_wakes_it_before_it_would_have_looked()
    {
        var setup = await SetUpAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxHandler>(setup.Handler);
        var signal = new OutboxSignal();
        var dispatcher = new OutboxDispatcher(
            NullLogger<OutboxDispatcher>.Instance,
            await setup.Tenant.NewClientAsync(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            signal,
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions { TenantId = setup.Tenant.TenantId, PollInterval = TimeSpan.FromMinutes(5) })
        );
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            // Let it look, find nothing, and go to sleep
            await Task.Delay(500);

            await setup.EnqueueAsync();
            signal.Notify();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (setup.Handler.Handled.IsEmpty && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            await Assert.That(setup.Handler.Handled.Count).IsEqualTo(1);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }
}
