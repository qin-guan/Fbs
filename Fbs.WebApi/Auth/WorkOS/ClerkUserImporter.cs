using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Telemetry;
using SqlSugar;

namespace Fbs.WebApi.Auth.WorkOS;

/// <summary>What <see cref="ClerkUserImporter"/> did, or would do in a dry run.</summary>
public sealed class ClerkUserImportReport
{
    public bool DryRun { get; init; }

    public int Read { get; set; }

    /// <summary>Made in WorkOS.</summary>
    public int Created { get; set; }

    /// <summary>In WorkOS already, from an earlier run, and brought up to date with Clerk, as they hadn't signed in with WorkOS yet.</summary>
    public int Updated { get; set; }

    /// <summary>In WorkOS already and signed in with it, so WorkOS is what is right about them now, and they were left as they are.</summary>
    public int AlreadyUsingWorkOS { get; set; }

    /// <summary>Whose password was moved, so they sign in with it as before.</summary>
    public int WithPassword { get; set; }

    /// <summary>Without a password: they signed in with Google, or their password can't be moved and they set a new one.</summary>
    public int WithoutPassword { get; set; }

    /// <summary>Whose account here was joined to their WorkOS user.</summary>
    public int Joined { get; set; }

    public int AlreadyJoined { get; set; }

    /// <summary>Who never signed in here, so have no account here to join: one is made the first time they do.</summary>
    public int NoAccountHere { get; set; }

    /// <summary>Whose account here was erased, which is never joined to anybody.</summary>
    public int ErasedHere { get; set; }

    public int Skipped { get; set; }

    /// <summary>The Clerk user IDs of accounts here whose Clerk user isn't in the export, who would be somebody new with WorkOS.</summary>
    public List<string> LeftBehind { get; } = [];

    public List<string> Warnings { get; } = [];

    /// <summary>What has to be put right, by hand, for somebody to be the account they had. The import can be run again after.</summary>
    public List<string> Errors { get; } = [];

    public IEnumerable<string> Summary()
    {
        yield return DryRun ? "Dry run: nothing was changed in WorkOS or here." : "Imported.";
        yield return $"Clerk users in the export: {Read}";
        yield return $"WorkOS users made: {Created}, brought up to date: {Updated}, left as they are as they sign in with WorkOS already: {AlreadyUsingWorkOS}";
        yield return $"With their password: {WithPassword}, without one: {WithoutPassword}";
        yield return $"Accounts here joined: {Joined}, joined already: {AlreadyJoined}, none here as they never signed in: {NoAccountHere}, erased here: {ErasedHere}";
        yield return $"Skipped: {Skipped}";
        foreach (var warning in Warnings)
        {
            yield return $"Warning: {warning}";
        }

        foreach (var error in Errors)
        {
            yield return $"Error: {error}";
        }
    }
}

/// <summary>
/// Moves everybody from Clerk to WorkOS without them doing anything: each is made a WorkOS user with their email address, name and
/// password (its bcrypt hash, so it is never known), and their Clerk user ID as the WorkOS user's external ID, and their account here is
/// joined to it. Somebody who signed in with Google signs in with Google again, and WorkOS finds them by their email address.
/// </summary>
/// <remarks>
/// <para>
/// It can be run as often as needed, and is run again just before the switch, for whoever signed up or changed something since. Until
/// somebody first signs in with WorkOS, Clerk is what is right about them, and their WorkOS user is brought up to date with it,
/// password included. After, WorkOS is, and they are left as they are, so somebody who has set a new password with WorkOS doesn't
/// get their old one back.
/// </para>
/// <para>
/// Nothing is said about anybody but by their Clerk user ID, so what it prints has no email addresses in it. See
/// docs/runbooks/cutover-3-workos.md.
/// </para>
/// </remarks>
public sealed class ClerkUserImporter(ISqlSugarClient sql, WorkOSUsers workOS)
{
    /// <summary>The only kind of password hash that is moved. It is what Clerk makes for a password set with it.</summary>
    public const string Bcrypt = "bcrypt";

    public async Task<ClerkUserImportReport> ImportAsync(IReadOnlyList<ClerkExportedUser> users, bool dryRun, CancellationToken ct)
    {
        var report = new ClerkUserImportReport { DryRun = dryRun, Read = users.Count };
        var withTotp = 0;
        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(user.Id))
            {
                report.Skipped++;
                report.Errors.Add("A row of the export has no id.");
                continue;
            }

            if (user.PrimaryEmail is null)
            {
                report.Skipped++;
                report.Warnings.Add($"{user.Id} has no email address, so can't sign in with WorkOS, and was skipped.");
                continue;
            }

            if (user.HasTotp)
            {
                withTotp++;
            }

            try
            {
                await ImportAsync(user, user.PrimaryEmail, dryRun, report, ct);
            }
            catch (WorkOSException e)
            {
                report.Skipped++;
                report.Errors.Add($"{user.Id}: {e.Message}");
            }
        }

        if (withTotp > 0)
        {
            report.Warnings.Add(
                $"{withTotp} had an authenticator app as a second step, which can't be moved: they sign in with their password, and set it up again with WorkOS if it asks for one."
            );
        }

        await FindLeftBehindAsync(users, report, ct);
        return report;
    }

    private async Task ImportAsync(ClerkExportedUser user, string email, bool dryRun, ClerkUserImportReport report, CancellationToken ct)
    {
        // A password is only moved for an address that is known to be theirs, or whoever signed up with somebody else's address in
        // Clerk, and never verified it, would have a way in to theirs once they sign in with Google
        var verified = !user.PrimaryEmailUnverified;
        string? password = null;
        if (user.PasswordDigest is not null && !verified)
        {
            report.Warnings.Add($"{user.Id} never verified their email address with Clerk, so their password was not moved: they set a new one, or sign in with Google.");
        }
        else if (user.PasswordDigest is not null && !string.Equals(user.PasswordHasher, Bcrypt, StringComparison.OrdinalIgnoreCase))
        {
            report.Warnings.Add($"{user.Id} has a password hashed with {user.PasswordHasher ?? "something unnamed"}, not {Bcrypt}, so it was not moved: they set a new one, or sign in with Google.");
        }
        else
        {
            password = user.PasswordDigest;
        }

        if (user.VerifiedEmails.Count(address => !string.Equals(address, email, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            report.Warnings.Add(
                $"{user.Id} has more than one email address with Clerk, and WorkOS keeps one, their primary one: signing in with Google with another of them would be somebody new."
            );
        }

        var existing = await workOS.GetByExternalIdAsync(user.Id, ct);
        if (existing is null)
        {
            existing = (await workOS.FindByEmailAsync(email, ct)).FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
            if (existing?.ExternalId is { Length: > 0 } other && other != user.Id)
            {
                report.Skipped++;
                report.Errors.Add($"{user.Id}: the WorkOS user {existing.Id} with their email address is another Clerk user's ({other}).");
                return;
            }
        }

        var changes = new WorkOSUserChanges
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            ExternalId = user.Id,
            PasswordHash = password,
            PasswordHashType = password is null ? null : Bcrypt,
        };

        WorkOSUser? workOSUser;
        if (existing is null)
        {
            workOSUser = dryRun ? null : await workOS.CreateAsync(changes with { Email = email, EmailVerified = verified }, ct);
            report.Created++;
        }
        else if (existing.LastSignInAt is null)
        {
            // Never signed in with WorkOS, so Clerk is still what is right about them. Never made unverified, as WorkOS may have
            // verified the address itself
            var update = changes with
            {
                Email = string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase) ? null : email,
                EmailVerified = verified && !existing.EmailVerified ? true : null,
            };
            workOSUser = dryRun ? existing : await workOS.UpdateAsync(existing.Id, update, ct);
            report.Updated++;
        }
        else
        {
            workOSUser = existing.ExternalId is null && !dryRun ? await workOS.UpdateAsync(existing.Id, new WorkOSUserChanges { ExternalId = user.Id }, ct) : existing;
            report.AlreadyUsingWorkOS++;
            password = null;
        }

        if (password is not null)
        {
            report.WithPassword++;
        }
        else if (existing?.LastSignInAt is null)
        {
            report.WithoutPassword++;
        }

        await JoinAsync(user.Id, workOSUser?.Id, dryRun, report, ct);
    }

    /// <summary>Joins their account here, if they have one, to their WorkOS user, which in a dry run that would make one isn't there yet.</summary>
    private async Task JoinAsync(string clerkUserId, string? workOSUserId, bool dryRun, ClerkUserImportReport report, CancellationToken ct)
    {
        var account = await sql.Queryable<UserAccount>().FirstAsync(a => a.ClerkUserId == clerkUserId, ct);
        if (account is null)
        {
            report.NoAccountHere++;
            return;
        }

        if (account.DeletedAt is not null)
        {
            report.ErasedHere++;
            return;
        }

        if (account.WorkOSUserId is not null && account.WorkOSUserId == workOSUserId)
        {
            report.AlreadyJoined++;
            return;
        }

        if (account.WorkOSUserId is { } other)
        {
            report.Errors.Add($"{clerkUserId}: their account here is joined to another WorkOS user ({other}).");
            return;
        }

        if (workOSUserId is null)
        {
            report.Joined++;
            return;
        }

        var taken = await sql.Queryable<UserAccount>().FirstAsync(a => a.WorkOSUserId == workOSUserId, ct);
        if (taken is not null)
        {
            report.Errors.Add(
                $"{clerkUserId}: the WorkOS user {workOSUserId} has an account here of its own ({taken.Id}), made when they signed in with WorkOS before this joined them. See the runbook."
            );
            return;
        }

        if (dryRun)
        {
            report.Joined++;
            return;
        }

        var accountId = account.Id;
        var joined = await sql.Updateable<UserAccount>()
            .SetColumns(a => new UserAccount { WorkOSUserId = workOSUserId })
            .Where(a => a.Id == accountId && a.WorkOSUserId == null)
            .ExecuteCommandAsync(ct);
        if (joined > 0)
        {
            report.Joined++;
            FbsMetrics.AccountsMoved.Add(1, new KeyValuePair<string, object?>("when", "import"));
        }
        else
        {
            // They signed in with WorkOS while this ran, and were joined then
            report.AlreadyJoined++;
        }
    }

    /// <summary>
    /// Accounts here of Clerk users who aren't in the export, and so would be somebody new with WorkOS: those deleted in Clerk without
    /// the webhook arriving, or an export from another Clerk instance.
    /// </summary>
    private async Task FindLeftBehindAsync(IReadOnlyList<ClerkExportedUser> users, ClerkUserImportReport report, CancellationToken ct)
    {
        var exported = users.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var unjoined = await sql.Queryable<UserAccount>()
            .Where(a => a.ClerkUserId != null && a.WorkOSUserId == null && a.DeletedAt == null)
            .Select(a => a.ClerkUserId)
            .ToListAsync(ct);
        report.LeftBehind.AddRange(unjoined.Where(id => !exported.Contains(id!)).Select(id => id!));
        if (report.LeftBehind.Count > 0)
        {
            report.Warnings.Add(
                $"{report.LeftBehind.Count} accounts here are of Clerk users who aren't in the export, and would be somebody new with WorkOS: "
                    + $"{string.Join(", ", report.LeftBehind.Take(20))}{(report.LeftBehind.Count > 20 ? ", ..." : "")}."
            );
        }
    }
}
