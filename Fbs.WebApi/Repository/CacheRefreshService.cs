namespace Fbs.WebApi.Repository;

/// <summary>
/// Loads bookings, users and facilities when the app starts, so the first requests don't wait on
/// Google, then keeps bookings in step with changes made to the calendar outside the API.
/// </summary>
public sealed class CacheRefreshService(
    ILogger<CacheRefreshService> logger,
    IServiceScopeFactory scopeFactory,
    BookingCache bookingCache
) : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await Task.WhenAll(
                bookingCache.SyncAsync(stoppingToken),
                LoadReferenceDataAsync(scope.ServiceProvider, stoppingToken)
            );
        }
        catch (Exception e) when (!stoppingToken.IsCancellationRequested)
        {
            // Requests will load whatever is missing instead
            logger.LogWarning(e, "Failed to load caches on startup");
        }

        using var timer = new PeriodicTimer(SyncInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await bookingCache.SyncAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(e, "Failed to sync bookings with the calendar");
            }
        }
    }

    /// <summary>
    /// One after the other, because queries started together from the same place share a database
    /// connection, and one of them fails saying it is already connecting.
    /// </summary>
    private static async Task LoadReferenceDataAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<IUserRepository>().GetListAsync(cancellationToken);
        await services.GetRequiredService<IFacilityRepository>().GetListAsync(cancellationToken);
    }
}
