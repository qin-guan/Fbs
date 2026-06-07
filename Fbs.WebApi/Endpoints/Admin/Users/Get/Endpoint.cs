using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Admin.Users.Get;

public class Endpoint(
    UserRepository userRepository
) : EndpointWithoutRequest<IEnumerable<Entities.User>>
{
    public override void Configure()
    {
        Get("/Admin/Users");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        if (phone is null)
        {
            throw new Exception("User does not exist");
        }

        var currentUser = await userRepository.FindAsync(u => u.Phone == phone, ct);
        if (currentUser?.IsAdmin != true)
        {
            throw new Exception("You do not have permission to access this resource.");
        }

        var users = await userRepository.GetListAsync(ct);
        await Send.OkAsync(users, ct);
    }
}
