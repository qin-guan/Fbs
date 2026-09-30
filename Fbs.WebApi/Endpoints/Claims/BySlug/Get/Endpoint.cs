using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Claims;

namespace Fbs.WebApi.Endpoints.Claims.BySlug.Get;

/// <summary>
/// Whether somebody signed in can claim their place in an organisation that came from the old version. If they can't,
/// whatever the reason, it isn't found.
/// </summary>
[RequiresClerk]
public class Endpoint(ICurrentAccount currentAccount, MemberClaims claims) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/Claims/{slug}");
        AuthSchemes(ClerkAuthentication.Scheme);
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var account = await currentAccount.GetAsync(ct);
        if (account is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var tenant = await claims.ClaimableAsync(req.Slug, account.Id, ct);
        if (tenant is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new Response { OrganizationName = tenant.Name }, ct);
    }
}
