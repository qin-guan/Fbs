using FastEndpoints;
using FastEndpoints.Security;

namespace Fbs.WebApi.Endpoints.Auth.Logout.Post;

public class Endpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/Auth/Logout");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await CookieAuth.SignOutAsync();
    }
}