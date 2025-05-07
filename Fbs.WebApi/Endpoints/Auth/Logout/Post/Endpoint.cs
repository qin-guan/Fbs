using System.Security.Claims;
using FastEndpoints;
using FastEndpoints.Security;

namespace Fbs.WebApi.Endpoints.Auth.Logout.Post;

public class Endpoint(ILogger<Endpoint> logger) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/Auth/Logout");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        logger.LogInformation("User {Phone} logged out", User.FindFirstValue("Phone"));
        await CookieAuth.SignOutAsync();
    }
}