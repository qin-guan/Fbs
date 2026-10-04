using FastEndpoints;
using Fbs.WebApi.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.TelegramLinks;

namespace Fbs.WebApi.Endpoints.Me.Telegram.Link.Post;

/// <summary>
/// Makes the link for connecting Telegram to the signed in account. It works once, for ten minutes, and a new one
/// replaces it. The chat that is connected already stays until the new one is opened.
/// </summary>
[RequiresClerk]
public class Endpoint(ICurrentAccount currentAccount, TelegramLinker linker) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Post("/Me/Telegram/Link");
        AuthSchemes(ClerkAuthentication.Scheme);
        Options(x => x.RequireRateLimiting(RateLimitPolicies.LinkTelegram));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var account = await currentAccount.GetAsync(ct);
        if (account is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var started = await linker.StartAsync(account.Id, ct);
        await Send.OkAsync(new Response { Url = started.Url, ExpiresAt = started.ExpiresAt }, ct);
    }
}
