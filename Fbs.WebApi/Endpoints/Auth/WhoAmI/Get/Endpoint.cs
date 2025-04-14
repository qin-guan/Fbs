using System.Security.Claims;
using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Auth.WhoAmI.Get;

public class Endpoint : EndpointWithoutRequest<List<Claim>>
{
    public override void Configure()
    {
        Post("/Auth/WhoAmI");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendOkAsync(User.Claims.ToList(), ct);
    }
}