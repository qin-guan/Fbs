using System.Threading.RateLimiting;

namespace Fbs.WebApi.Notifications;

/// <summary>
/// Keeps the messages sent to Telegram, all of them together, under what it allows a bot, which is about
/// 30 a second, so telling 50 people about a batch of bookings can't get the bot told to stop.
/// </summary>
public sealed class TelegramThrottle : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public TelegramThrottle(IConfiguration configuration)
    {
        var perSecond = Math.Max(1, configuration.GetValue("Telegram:MessagesPerSecond", 25));
        _limiter = new TokenBucketRateLimiter(
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = perSecond,
                TokensPerPeriod = perSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = int.MaxValue,
                AutoReplenishment = true,
            }
        );
    }

    /// <summary>Waits until a message can be sent.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(1, cancellationToken);
    }

    public void Dispose() => _limiter.Dispose();
}
