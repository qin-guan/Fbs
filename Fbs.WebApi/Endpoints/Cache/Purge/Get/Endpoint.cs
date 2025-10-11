using FastEndpoints;
using Microsoft.Extensions.Caching.Hybrid;

namespace Fbs.WebApi.Endpoints.Cache.Purge.Get;

public class Endpoint(HybridCache cache) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/Cache/Purge");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await cache.RemoveAsync(["Facilities", "Bookings", "Nominal Roll", "Users"], ct);
        await Send.OkAsync(ct);
    }
}