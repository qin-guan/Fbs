using FastEndpoints;
using Fbs.WebApi.Repository;
using Microsoft.Extensions.Caching.Hybrid;

namespace Fbs.WebApi.Endpoints.Cache.Purge.Get;

public class Endpoint(HybridCache cache, BookingCache bookingCache) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/Cache/Purge");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await cache.RemoveAsync(
            ["Facilities", "Nominal Roll", "Users", "OTPs Sheet ID", "Users Sheet ID"],
            ct
        );
        await bookingCache.ReloadAsync(ct);
        await Send.OkAsync(cancellation: ct);
    }
}
