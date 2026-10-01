using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Repository;
using Microsoft.Extensions.Caching.Hybrid;

namespace Fbs.WebApi.Endpoints.Cache.Purge.Get;

/// <summary>
/// Drops everything read from Google, so changes made straight to the spreadsheet or calendar
/// show up now. Each purge reloads every booking, so it is only for admins.
/// </summary>
public class Endpoint(HybridCache cache, IBookingService bookingService, IUserRepository userRepository)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/Cache/Purge");
        Claims("Phone");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        var currentUser = await userRepository.FindAsync(u => u.Phone == phone, ct);
        if (currentUser?.IsAdmin != true)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        await cache.RemoveAsync(
            ["Facilities", "Nominal Roll", "Users", "OTPs Sheet ID", "Users Sheet ID"],
            ct
        );
        await bookingService.ReloadAsync(ct);
        await Send.OkAsync(cancellation: ct);
    }
}
