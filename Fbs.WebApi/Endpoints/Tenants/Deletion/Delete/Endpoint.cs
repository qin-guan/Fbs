using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Tenants.Deletion.Delete;

/// <summary>An admin takes back asking for their organisation to be deleted, while it is still there.</summary>
[RequiresAccounts]
public class Endpoint(ICurrentAccount currentAccount, TenantDeletions deletions) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/Tenants/{slug}/Deletion");
        AuthSchemes(AccountAuthentication.Scheme);
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var account = await currentAccount.GetAsync(ct);
        if (account is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        switch (await deletions.RestoreAsync(req.Slug, account.Id, ct))
        {
            case DeletionOutcome.NotFound:
                await Send.NotFoundAsync(ct);
                return;
            case DeletionOutcome.NotInThatState:
                AddError("It isn't to be deleted.", "not-pending-deletion");
                await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
                return;
        }

        await Send.NoContentAsync(ct);
    }
}
