using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Tenants.Deletion.Post;

/// <summary>
/// An admin asks for their organisation to be deleted: nobody can use it from then, and it is deleted for good after
/// <c>Limits:DeletionGraceDays</c>, unless an admin restores it.
/// </summary>
[RequiresClerk]
public class Endpoint(ICurrentAccount currentAccount, TenantDeletions deletions) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/Tenants/{slug}/Deletion");
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

        var (outcome, deleteAfter) = await deletions.RequestAsync(req.Slug, account.Id, ct);
        switch (outcome)
        {
            case DeletionOutcome.NotFound:
                await Send.NotFoundAsync(ct);
                return;
            case DeletionOutcome.NotInThatState:
                AddError("It can't be deleted now: it is to be deleted already, or has been suspended.", "not-active");
                await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
                return;
        }

        await Send.OkAsync(new Response { DeleteAfter = deleteAfter!.Value }, ct);
    }
}
