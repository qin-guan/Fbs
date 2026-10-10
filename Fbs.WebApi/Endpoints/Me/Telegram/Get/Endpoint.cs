using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.TelegramLinks;

namespace Fbs.WebApi.Endpoints.Me.Telegram.Get;

[RequiresAccounts]
public class Endpoint(ICurrentAccount currentAccount, TelegramLinker linker) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/Me/Telegram");
        AuthSchemes(AccountAuthentication.Scheme);
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
