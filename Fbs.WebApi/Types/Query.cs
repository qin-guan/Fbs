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
        ClaimsPrincipal claimsPrincipal,
        UserRepository userRepository,
        IResolverContext context,
        CancellationToken ct
    )
    {
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
        string? name,
        FacilityRepository facilityRepository,
        CancellationToken ct
    )
    {
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

    public static async Task<List<Booking>> GetBookings(
        Guid? id,
        string? userPhone,
        BookingRepository bookingRepository,
        CancellationToken ct
    )
    {
        var all = await bookingRepository.GetListAsync(ct);

        if (id is not null)
        {
            all = all.Where(b => b.Id == id).ToList();
        }

        if (userPhone is not null)
        {
            all = all.Where(b => b.UserPhone == userPhone).ToList();
        }

        return all;
    }
}