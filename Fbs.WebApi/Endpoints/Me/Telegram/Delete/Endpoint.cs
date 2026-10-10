using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.TelegramLinks;

namespace Fbs.WebApi.Endpoints.Me.Telegram.Delete;

/// <summary>Stops notifications going to Telegram, and a link that was made and not used from working.</summary>
[RequiresAccounts]
public class Endpoint(ICurrentAccount currentAccount, TelegramLinker linker) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/Me/Telegram");
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

        await linker.UnlinkAsync(account.Id, ct);
        await Send.NoContentAsync(ct);
    }
}
