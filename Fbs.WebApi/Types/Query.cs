using System.Security.Claims;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Authorization;

namespace Fbs.WebApi.Types;

[QueryType]
public static class Query
{
    [Authorize]
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
        FacilityRepository facilityRepository,
        CancellationToken ct
    )
    {
        return await facilityRepository.GetListAsync(ct);
    }
}