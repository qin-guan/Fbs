using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;
using HotChocolate.Resolvers;

namespace Fbs.WebApi.Types;

[HotChocolate.Authorization.Authorize]
[QueryType]
public static class Query
{
    public static async Task<User?> GetMe(
        InstrumentationSource instrumentation,
        ClaimsPrincipal claimsPrincipal,
        UserRepository userRepository,
        IResolverContext context,
        CancellationToken ct
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();
        
        var phone = claimsPrincipal.FindFirstValue("Phone");
        if (phone is null)
        {
            throw new Exception("Phone does not exist in principal");
        }

        var user = await userRepository.FindAsync(u => u.Phone == phone, ct);
        if (user is null)
        {
            context.ReportError(ErrorBuilder.New().SetMessage("User does not exist.").Build());
        }

        return user;
    }

    public static async Task<List<Facility>> GetFacilities(
        InstrumentationSource instrumentation,
        string? name,
        FacilityRepository facilityRepository,
        CancellationToken ct
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        if (name is null)
        {
            return await facilityRepository.GetListAsync(ct);
        }
        
        var facility = await facilityRepository.FindAsync(f => f.Name == name, ct);
        if (facility is null)
        {
            return [];
        }

        return [facility];
    }

    public static async Task<BookingWithUser> GetBooking(
        InstrumentationSource instrumentation,
        Guid id,
        BookingRepository bookingRepository,
        UserRepository userRepository,
        CancellationToken ct
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();
        var booking = await bookingRepository.GetAsync(b => b.Id == id, ct);

        return new BookingWithUser
        {
            Id = booking.Id,
            FacilityName = booking.FacilityName,
            Conduct = booking.Conduct,
            Description = booking.Description,
            PocName = booking.PocName,
            PocPhone = booking.PocPhone,
            StartDateTime = booking.StartDateTime,
            EndDateTime = booking.EndDateTime,
            User = await userRepository.FindAsync(u => u.Phone == booking.UserPhone, ct),
        };
    }

    public static async Task<List<BookingWithUser>> GetBookings(
        string? userPhone,
        DateTimeOffset? startsAfter,
        InstrumentationSource instrumentation,
        BookingRepository bookingRepository,
        UserRepository userRepository,
        CancellationToken ct
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();
        var all = await bookingRepository.GetListAsync(ct);

        if (userPhone is not null)
        {
            all = all.Where(b => b.UserPhone == userPhone).ToList();
        }

        if (startsAfter is not null)
        {
            all = all.Where(b => b.StartDateTime >= startsAfter).ToList();
        }

        var users = await userRepository.GetListAsync(ct);
        var withUser = all.Select(booking => new BookingWithUser
        {
            Id = booking.Id,
            FacilityName = booking.FacilityName,
            Conduct = booking.Conduct,
            Description = booking.Description,
            PocName = booking.PocName,
            PocPhone = booking.PocPhone,
            StartDateTime = booking.StartDateTime,
            EndDateTime = booking.EndDateTime,
            User = users.Single(u => u.Phone == booking.UserPhone),
        });

        return withUser.OrderByDescending(b => b.StartDateTime).ToList();
    }
}