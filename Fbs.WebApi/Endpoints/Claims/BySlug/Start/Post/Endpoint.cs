using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Claims;

namespace Fbs.WebApi.Endpoints.Claims.BySlug.Start.Post;

/// <summary>
/// Makes the Telegram link the user opens to claim their imported member (see <see cref="MemberClaims"/>). It works once, for 10
/// minutes, and replaces any link they had for this organisation. 404 when <c>GET /Claims/{slug}</c> would be.
/// </summary>
[RequiresClerk]
public class Endpoint(ICurrentAccount currentAccount, MemberClaims claims) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/Claims/{slug}/Start");
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

        var started = await claims.StartAsync(account.Id, tenant, ct);
        await Send.OkAsync(new Response { Url = started.Url, ExpiresAt = started.ExpiresAt }, ct);
    }
}
