using System.Threading.Channels;

namespace Fbs.WebApi.Outbox;

/// <summary>
/// Wakes the dispatcher when there is something new, so a message doesn't wait for the next poll.
/// Nothing depends on it: the dispatcher polls anyway, and another instance may have written the message.
/// </summary>
public sealed class OutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
    );

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Waits until <see cref="Notify"/> is called, or the time is up.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }
}
