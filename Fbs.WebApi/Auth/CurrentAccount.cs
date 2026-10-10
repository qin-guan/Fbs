using System.Security.Claims;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Auth.WorkOS;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Telemetry;
using SqlSugar;

namespace Fbs.WebApi.Auth;

/// <summary>Who is making the request, as an account, made the first time they are seen.</summary>
public interface ICurrentAccount
{
    /// <summary>
    /// Their account, or null if the request isn't signed in with an account, or the account has been deleted. A
    /// name or email that has changed with Clerk or WorkOS is changed here.
    /// </summary>
    Task<UserAccount?> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class CurrentAccount(IHttpContextAccessor httpContextAccessor, ISqlSugarClient sql, ILogger<CurrentAccount> logger) : ICurrentAccount
{
    private UserAccount? _account;
    private bool _resolved;

    public async Task<UserAccount?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved)
        {
            return _account;
        }

        var principal = httpContextAccessor.HttpContext?.User;
        var subject = principal?.FindFirstValue("sub");
        if (principal?.Identity is not { IsAuthenticated: true, AuthenticationType: ClerkAuthentication.Scheme or WorkOSAuthentication.Scheme } identity || string.IsNullOrEmpty(subject))
        {
            _resolved = true;
            return null;
        }

        var provider = identity.AuthenticationType;
        var name = Trim(principal.FindFirstValue("name") ?? FullName(principal.FindFirstValue("given_name"), principal.FindFirstValue("family_name")), 200);
        var email = Trim(principal.FindFirstValue("email"), 320);

        var account = provider == ClerkAuthentication.Scheme
            ? await sql.Queryable<UserAccount>().FirstAsync(a => a.ClerkUserId == subject, cancellationToken)
            : await FindWorkOSAccountAsync(subject, principal.FindFirstValue(WorkOSAuthentication.ClerkUserIdClaim), cancellationToken);
        if (account is null)
        {
            account = new UserAccount
            {
                Id = Guid.NewGuid(),
                ClerkUserId = provider == ClerkAuthentication.Scheme ? subject : null,
                WorkOSUserId = provider == WorkOSAuthentication.Scheme ? subject : null,
                Name = name,
                Email = email,
            };
            try
            {
                await sql.Insertable(account).ExecuteCommandAsync(cancellationToken);
                FbsMetrics.AccountsCreated.Add(1);
            }
            catch (Exception e) when (e.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
            {
                // A request at the same moment made it first
                account = provider == ClerkAuthentication.Scheme
                    ? await sql.Queryable<UserAccount>().FirstAsync(a => a.ClerkUserId == subject, cancellationToken)
                    : await sql.Queryable<UserAccount>().FirstAsync(a => a.WorkOSUserId == subject, cancellationToken);
            }
        }
        else if (account.DeletedAt is null && ((name is not null && account.Name != name) || (email is not null && account.Email != email)))
        {
            account.Name = name ?? account.Name;
            account.Email = email ?? account.Email;
            await sql.Updateable(account).UpdateColumns(a => new { a.Name, a.Email }).ExecuteCommandAsync(cancellationToken);
        }

        _resolved = true;
        if (account.DeletedAt is not null)
        {
            logger.LogWarning("A deleted account {AccountId} was used with a session token that hasn't expired", account.Id);
            return _account = null;
        }

        return _account = account;
    }

    /// <summary>
    /// The account of somebody signed in with WorkOS. The first time somebody moved from Clerk is seen, if <c>import-clerk-users</c>
    /// didn't already join the two, their WorkOS user is joined to the account they had with Clerk, which their token names: it is
    /// set on the WorkOS user by the import, and only WorkOS can put it in a token. An account is only ever joined to one WorkOS user,
    /// and never once it has been erased, as that person deleted it.
    /// </summary>
    private async Task<UserAccount?> FindWorkOSAccountAsync(string workOSUserId, string? clerkUserId, CancellationToken cancellationToken)
    {
        var account = await sql.Queryable<UserAccount>().FirstAsync(a => a.WorkOSUserId == workOSUserId, cancellationToken);
        if (account is not null || string.IsNullOrEmpty(clerkUserId))
        {
            return account;
        }

        var joined = await sql.Updateable<UserAccount>()
            .SetColumns(a => new UserAccount { WorkOSUserId = workOSUserId })
            .Where(a => a.ClerkUserId == clerkUserId && a.WorkOSUserId == null && a.DeletedAt == null)
            .ExecuteCommandAsync(cancellationToken);
        if (joined > 0)
        {
            FbsMetrics.AccountsMoved.Add(1, new KeyValuePair<string, object?>("when", "sign_in"));
            logger.LogInformation("Joined a WorkOS user to the account they had with Clerk on their first sign in");
        }

        // Joined now, or by a request at the same moment
        return await sql.Queryable<UserAccount>().FirstAsync(a => a.WorkOSUserId == workOSUserId, cancellationToken);
    }

    private static string? FullName(string? given, string? family) =>
        string.Join(' ', new[] { given, family }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim())) is { Length: > 0 } full ? full : null;

    private static string? Trim(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { } trimmed && trimmed.Length > length ? trimmed[..length] : value.Trim();
}
