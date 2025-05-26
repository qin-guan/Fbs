using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Auth.Me.Get;

public class Endpoint(
    UserRepository userRepository
) : EndpointWithoutRequest<User>
{
    public override void Configure()
    {
        Get("/Auth/Me");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
        await SendOkAsync(user, ct);
    }
}