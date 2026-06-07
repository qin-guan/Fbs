using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Admin.Users.Post;

public class Request
{
    public string Phone { get; set; } = string.Empty;
}

public class Endpoint(UserRepository userRepository) : Endpoint<Request, Entities.User>
{
    public override void Configure()
    {
        Post("/Admin/Users/{Phone}/Admin");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        if (phone is null)
        {
            throw new Exception("User does not exist");
        }

        var currentUser = await userRepository.FindAsync(u => u.Phone == phone, ct);
        if (currentUser?.IsAdmin != true)
        {
            throw new Exception("You do not have permission to manage admin status.");
        }

        var targetUser = await userRepository.FindAsync(u => u.Phone == req.Phone, ct);
        if (targetUser is null)
        {
            throw new Exception("User does not exist.");
        }

        targetUser.IsAdmin = !targetUser.IsAdmin;
        await userRepository.UpdateAsync(targetUser, ct);

        await Send.OkAsync(targetUser, ct);
    }
}
