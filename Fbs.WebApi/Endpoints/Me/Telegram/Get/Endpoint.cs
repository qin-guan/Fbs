using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.TelegramLinks;

namespace Fbs.WebApi.Endpoints.Me.Telegram.Get;

[RequiresClerk]
public class Endpoint(ICurrentAccount currentAccount, TelegramLinker linker) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/Me/Telegram");
        AuthSchemes(ClerkAuthentication.Scheme);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var account = await currentAccount.GetAsync(ct);
        if (account is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var status = await linker.GetAsync(account.Id, ct);
        await Send.OkAsync(new Response { Linked = status.Linked, LinkedAt = status.LinkedAt }, ct);
    }
}
