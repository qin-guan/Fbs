using System.Security.Claims;
using Fbs.WebApi.Data.Entities;
using SqlSugar;

namespace Fbs.WebApi.Auth.Clerk;

/// <summary>Who is making the request, as an account, made the first time they are seen.</summary>
public interface ICurrentAccount
{
    /// <summary>
    /// Their account, or null if the request isn't signed in with Clerk, or the account has been deleted. A
    /// name or email that has changed with Clerk is changed here.
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
        var clerkUserId = principal?.FindFirstValue("sub");
        if (principal?.Identity is not { IsAuthenticated: true, AuthenticationType: ClerkAuthentication.Scheme } || string.IsNullOrEmpty(clerkUserId))
        {
            _resolved = true;
            return null;
        }

        var name = Trim(principal.FindFirstValue("name"), 200);
        var email = Trim(principal.FindFirstValue("email"), 320);

        var account = await sql.Queryable<UserAccount>().FirstAsync(a => a.ClerkUserId == clerkUserId, cancellationToken);
        if (account is null)
        {
            account = new UserAccount { Id = Guid.NewGuid(), ClerkUserId = clerkUserId, Name = name, Email = email };
            try
            {
                await sql.Insertable(account).ExecuteCommandAsync(cancellationToken);
            }
            catch (Exception e) when (e.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
            {
                // A request at the same moment made it first
                account = await sql.Queryable<UserAccount>().FirstAsync(a => a.ClerkUserId == clerkUserId, cancellationToken);
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

    private static string? Trim(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { } trimmed && trimmed.Length > length ? trimmed[..length] : value.Trim();
}
