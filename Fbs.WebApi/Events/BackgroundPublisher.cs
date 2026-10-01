using System.Threading.Channels;
using FastEndpoints;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Events;

public sealed class EventOptions
{
    /// <summary>
    /// Whether events are published at all. Off when bookings are in the database, which tells people about
    /// them through the outbox, inside the same transaction, instead. Goes with the events.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Publishes events one at a time in the background, so requests don't wait for the Telegram
/// messages they trigger to be sent.
/// </summary>
public sealed class BackgroundPublisher(
    ILogger<BackgroundPublisher> logger,
    IServiceProvider serviceProvider,
    IOptions<EventOptions> options
) : IHostedLifecycleService
{
    private readonly Channel<(object Event, Func<Task> Publish)> _queue = Channel.CreateUnbounded<(
        object,
        Func<Task>
    )>(new UnboundedChannelOptions { SingleReader = true });

    private Task _publishing = Task.CompletedTask;

    public void Publish<TEvent>(TEvent @event)
        where TEvent : notnull
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var bus = serviceProvider.GetRequiredService<EventBus<TEvent>>();
        if (!_queue.Writer.TryWrite((@event, () => bus.PublishAsync(@event, Mode.WaitForAll))))
        {
            logger.LogError("Dropped {Event} as the app has stopped", typeof(TEvent).Name);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _publishing = Task.Run(PublishQueuedAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs once the server has finished handling requests, so nothing more can be queued, and
    /// waits for the events that already are to be published.
    /// </summary>
    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        await _publishing.WaitAsync(cancellationToken);
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task PublishQueuedAsync()
    {
        await foreach (var (@event, publish) in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await publish();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to publish {Event}", @event.GetType().Name);
            }
        }
    }
}
