extern alias Migrator;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Auth.WorkOS;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using ImportClerkUsersCommand = Migrator::Fbs.DbMigrator.Commands.ImportClerkUsersCommand;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Moving everybody from Clerk to WorkOS without them doing anything: their WorkOS user has their email address, name, password and
/// Clerk user ID, and their account here is joined to it, so signing in with WorkOS is the account they had.
/// </summary>
public class ClerkUserImportTests
{
    [ClassDataSource<MovingFbsApiFactory>]
    public required MovingFbsApiFactory Factory { get; init; }

    /// <summary>A bcrypt hash, as Clerk exports it.</summary>
    private const string Hash = "$2a$10$fgPOubgdJs90F1GK2u.Qye7Xyyi2ajrQJoB5LDG5bnITEvFR/xhZ.";

    private const string Header =
        "id,first_name,last_name,username,primary_email_address,primary_phone_number,verified_email_addresses,unverified_email_addresses,verified_phone_numbers,unverified_phone_numbers,totp_secret,password_digest,password_hasher";

    private readonly FakeWorkOS _workOS = new();

    private static string Row(
        string id,
        string email,
        string? first = "Some",
        string? last = "One",
        string? verified = "",
        string? unverified = null,
        string? totp = null,
        string? digest = Hash,
        string? hasher = "bcrypt"
    ) => $"{id},{first},{last},,{email},,{(verified == "" ? email : verified)},{unverified},,,{totp},{Quoted(digest)},{(digest is null ? null : hasher)}";

    private static string? Quoted(string? field) => field?.Contains(',') == true ? $"\"{field}\"" : field;

    private static IReadOnlyList<ClerkExportedUser> Export(params string[] rows) => ClerkExport.ReadCsv(string.Join("\n", [Header, .. rows]));

    private ClerkUserImporter Importer() => new(Factory.Db, _workOS.Client());

    private static string Email() => $"{Guid.NewGuid():N}@example.com";

    /// <summary>Somebody who signed up with Clerk and signed in here, and is the admin of an organisation.</summary>
    private async Task<(string ClerkUserId, Guid AccountId, TestOrg Org)> SignedUpAsync()
    {
        var org = await Factory.CreateOrgAsync();
        var accountId = await Factory.AccountIdOfAsync(org.Admin);
        return (Factory.Db.Queryable<UserAccount>().First(a => a.Id == accountId).ClerkUserId!, accountId, org);
    }

    private UserAccount Account(Guid id) => Factory.Db.Queryable<UserAccount>().First(a => a.Id == id);

    [Test]
    public async Task Somebody_moved_has_their_password_and_name_in_workos_and_signing_in_with_it_is_the_account_they_had()
    {
        var (clerkUserId, accountId, org) = await SignedUpAsync();
        var email = Email();
        using var metrics = new MetricsRecorder();

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, email, "Ada", "Lovelace")), dryRun: false, CancellationToken.None);

        var user = _workOS.ByExternalId(clerkUserId)!;
        await Assert.That(user.Email).IsEqualTo(email);
        await Assert.That(user.FirstName).IsEqualTo("Ada");
        await Assert.That(user.LastName).IsEqualTo("Lovelace");
        await Assert.That(user.EmailVerified).IsTrue();
        await Assert.That(user.PasswordHash).IsEqualTo(Hash);
        await Assert.That(user.PasswordHashType).IsEqualTo("bcrypt");
        await Assert.That(Account(accountId).WorkOSUserId).IsEqualTo(user.Id);
        await Assert.That(report.Created).IsEqualTo(1);
        await Assert.That(report.WithPassword).IsEqualTo(1);
        await Assert.That(report.Joined).IsEqualTo(1);
        await Assert.That(report.Errors).IsEmpty();
        await Assert.That(metrics.Sum("fbs.accounts.moved", ("when", "import"))).IsEqualTo(1);

        // Without anything else in their token, they are the account they had, in the organisation they run
        using var client = Factory.ClientWithWorkOS(Factory.WorkOS.Token(user.Id, email, "Ada", "Lovelace"));
        var me = await client.GetFromJsonAsync<JsonElement>("/Me");
        await Assert.That(me.GetProperty("id").GetGuid()).IsEqualTo(accountId);
        await Assert.That(me.GetProperty("memberships").EnumerateArray().Single().GetProperty("tenantSlug").GetString()).IsEqualTo(org.Slug);
        await Assert.That(await client.GetAsync($"/t/{org.Slug}/Settings")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Somebody_who_signed_in_with_google_is_moved_without_a_password_and_finds_their_account_by_email()
    {
        var (clerkUserId, accountId, _) = await SignedUpAsync();

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, Email(), digest: null)), dryRun: false, CancellationToken.None);

        var user = _workOS.ByExternalId(clerkUserId)!;
        await Assert.That(user.PasswordHash).IsNull();
        await Assert.That(user.EmailVerified).IsTrue();
        await Assert.That(Account(accountId).WorkOSUserId).IsEqualTo(user.Id);
        await Assert.That(report.WithoutPassword).IsEqualTo(1);
    }

    [Test]
    public async Task Somebody_who_never_signed_in_here_is_moved_and_has_an_account_made_when_they_do()
    {
        var clerkUserId = ClerkFbsApiFactory.NewUserId();

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, Email())), dryRun: false, CancellationToken.None);

        await Assert.That(_workOS.ByExternalId(clerkUserId)).IsNotNull();
        await Assert.That(report.NoAccountHere).IsEqualTo(1);
        await Assert.That(Factory.Db.Queryable<UserAccount>().Any(a => a.ClerkUserId == clerkUserId)).IsFalse();
    }

    [Test]
    public async Task Running_it_again_makes_nobody_twice_and_brings_whoever_has_not_signed_in_with_workos_up_to_date()
    {
        var (clerkUserId, accountId, _) = await SignedUpAsync();
        var email = Email();
        await Importer().ImportAsync(Export(Row(clerkUserId, email)), dryRun: false, CancellationToken.None);
        var first = _workOS.ByExternalId(clerkUserId)!.Id;

        // They changed their password and name with Clerk before the switch
        var changed = "$2a$10$abcdefghijklmnopqrstuuJYCU3bH3KlxSL6OaIq2jlKTqLTKrTGW";
        var report = await Importer().ImportAsync(Export(Row(clerkUserId, email, "New", "Name", digest: changed)), dryRun: false, CancellationToken.None);

        await Assert.That(_workOS.Users.Count(u => u.ExternalId == clerkUserId)).IsEqualTo(1);
        var user = _workOS.ByExternalId(clerkUserId)!;
        await Assert.That(user.Id).IsEqualTo(first);
        await Assert.That(user.PasswordHash).IsEqualTo(changed);
        await Assert.That(user.FirstName).IsEqualTo("New");
        await Assert.That(report.Created).IsEqualTo(0);
        await Assert.That(report.Updated).IsEqualTo(1);
        await Assert.That(report.AlreadyJoined).IsEqualTo(1);
        await Assert.That(Account(accountId).WorkOSUserId).IsEqualTo(first);
    }

    [Test]
    public async Task Once_somebody_has_signed_in_with_workos_running_it_again_leaves_them_as_they_are()
    {
        var (clerkUserId, _, _) = await SignedUpAsync();
        var email = Email();
        await Importer().ImportAsync(Export(Row(clerkUserId, email)), dryRun: false, CancellationToken.None);
        var user = _workOS.ByExternalId(clerkUserId)!;
        // They signed in with WorkOS, and set a new password there
        user.LastSignInAt = DateTimeOffset.UtcNow;
        user.PasswordHash = "set-with-workos";
        var requests = _workOS.Requests.Count;

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, email, "Old", "Name")), dryRun: false, CancellationToken.None);

        await Assert.That(user.PasswordHash).IsEqualTo("set-with-workos");
        await Assert.That(user.FirstName).IsEqualTo("Some");
        await Assert.That(report.AlreadyUsingWorkOS).IsEqualTo(1);
        await Assert.That(_workOS.Requests.Skip(requests).Any(r => r.StartsWith("PUT") || r.StartsWith("POST"))).IsFalse();
    }

    [Test]
    public async Task Somebody_who_signed_up_with_workos_before_they_were_moved_keeps_what_they_set_and_gets_their_clerk_user_id()
    {
        var clerkUserId = ClerkFbsApiFactory.NewUserId();
        var email = Email();
        var existing = _workOS.Add(email, lastSignInAt: DateTimeOffset.UtcNow);

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, email)), dryRun: false, CancellationToken.None);

        await Assert.That(existing.ExternalId).IsEqualTo(clerkUserId);
        await Assert.That(existing.PasswordHash).IsNull();
        await Assert.That(report.AlreadyUsingWorkOS).IsEqualTo(1);
        await Assert.That(_workOS.Users.Count(u => u.Email == email)).IsEqualTo(1);
    }

    [Test]
    public async Task A_password_that_is_not_bcrypt_or_for_an_address_never_verified_is_not_moved_but_they_are()
    {
        var other = ClerkFbsApiFactory.NewUserId();
        var unverified = ClerkFbsApiFactory.NewUserId();
        var unverifiedEmail = Email();

        var report = await Importer()
            .ImportAsync(
                Export(Row(other, Email(), digest: "$argon2id$v=19$m=65536,t=3,p=4$abc$def", hasher: "argon2id"), Row(unverified, unverifiedEmail, verified: null, unverified: unverifiedEmail)),
                dryRun: false,
                CancellationToken.None
            );

        await Assert.That(_workOS.ByExternalId(other)!.PasswordHash).IsNull();
        await Assert.That(_workOS.ByExternalId(unverified)!.PasswordHash).IsNull();
        await Assert.That(_workOS.ByExternalId(unverified)!.EmailVerified).IsFalse();
        await Assert.That(report.WithoutPassword).IsEqualTo(2);
        await Assert.That(report.Warnings.Count(w => w.Contains(other) && w.Contains("argon2id"))).IsEqualTo(1);
        await Assert.That(report.Warnings.Count(w => w.Contains(unverified) && w.Contains("verified"))).IsEqualTo(1);
        await Assert.That(report.Errors).IsEmpty();
    }

    [Test]
    public async Task What_cannot_be_moved_is_said_by_clerk_user_id_and_never_by_email_address()
    {
        var multi = ClerkFbsApiFactory.NewUserId();
        var totp = ClerkFbsApiFactory.NewUserId();
        var email = Email();

        var report = await Importer().ImportAsync(Export(Row(multi, email, verified: $"{email}|other-{email}"), Row(totp, Email(), totp: "JBSWY3DPEHPK3PXP"), $"{ClerkFbsApiFactory.NewUserId()},No,Email,,,,,,,,,,"), dryRun: false, CancellationToken.None);

        await Assert.That(report.Warnings.Count(w => w.Contains(multi) && w.Contains("more than one email address"))).IsEqualTo(1);
        await Assert.That(report.Warnings.Count(w => w.Contains("1 had an authenticator app"))).IsEqualTo(1);
        await Assert.That(report.Warnings.Count(w => w.Contains("has no email address"))).IsEqualTo(1);
        await Assert.That(report.Skipped).IsEqualTo(1);
        await Assert.That(report.Summary().Any(line => line.Contains("@example.com"))).IsFalse();
    }

    [Test]
    public async Task A_workos_user_with_their_email_that_is_somebody_elses_is_an_error_and_nothing_is_joined()
    {
        var (clerkUserId, accountId, _) = await SignedUpAsync();
        var email = Email();
        _workOS.Add(email, externalId: "user_somebody_else");

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, email)), dryRun: false, CancellationToken.None);

        await Assert.That(report.Errors.Single()).Contains(clerkUserId);
        await Assert.That(Account(accountId).WorkOSUserId).IsNull();
        await Assert.That(_workOS.Users.Count(u => u.Email == email)).IsEqualTo(1);
    }

    [Test]
    public async Task Somebody_who_already_has_an_account_of_their_own_with_workos_is_an_error_and_not_joined()
    {
        var (clerkUserId, accountId, _) = await SignedUpAsync();
        var email = Email();
        // They signed in with WorkOS before they were moved, and without the Clerk user ID in their token, so they are somebody new here
        var existing = _workOS.Add(email, lastSignInAt: DateTimeOffset.UtcNow);
        using var client = Factory.ClientWithWorkOS(Factory.WorkOS.Token(existing.Id));
        var separate = (await client.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid();

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, email)), dryRun: false, CancellationToken.None);

        await Assert.That(report.Errors.Single()).Contains(separate.ToString());
        await Assert.That(Account(accountId).WorkOSUserId).IsNull();
        await Assert.That(Account(separate).WorkOSUserId).IsEqualTo(existing.Id);
    }

    [Test]
    public async Task An_account_here_that_was_erased_is_not_joined()
    {
        var (clerkUserId, accountId, _) = await SignedUpAsync();
        Factory.Db.Updateable<UserAccount>().SetColumns(a => new UserAccount { DeletedAt = DateTimeOffset.UtcNow }).Where(a => a.Id == accountId).ExecuteCommand();

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, Email())), dryRun: false, CancellationToken.None);

        await Assert.That(report.ErasedHere).IsEqualTo(1);
        await Assert.That(Account(accountId).WorkOSUserId).IsNull();
    }

    [Test]
    public async Task A_dry_run_reads_but_changes_nothing_in_workos_or_here()
    {
        var (clerkUserId, accountId, _) = await SignedUpAsync();

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, Email())), dryRun: true, CancellationToken.None);

        await Assert.That(report.Created).IsEqualTo(1);
        await Assert.That(report.Joined).IsEqualTo(1);
        await Assert.That(_workOS.Users).IsEmpty();
        await Assert.That(_workOS.Requests.All(r => r.StartsWith("GET"))).IsTrue();
        await Assert.That(Account(accountId).WorkOSUserId).IsNull();
        await Assert.That(report.Summary().First()).StartsWith("Dry run");
    }

    [Test]
    public async Task Accounts_here_of_clerk_users_not_in_the_export_are_named()
    {
        var (left, _, _) = await SignedUpAsync();
        var (moved, _, _) = await SignedUpAsync();

        var report = await Importer().ImportAsync(Export(Row(moved, Email())), dryRun: false, CancellationToken.None);

        await Assert.That(report.LeftBehind).Contains(left);
        await Assert.That(report.LeftBehind).DoesNotContain(moved);
    }

    [Test]
    public async Task Being_told_to_wait_by_workos_waits_and_carries_on()
    {
        var clerkUserId = ClerkFbsApiFactory.NewUserId();
        _workOS.TooManyRequests = 3;

        var report = await Importer().ImportAsync(Export(Row(clerkUserId, Email())), dryRun: false, CancellationToken.None);

        await Assert.That(report.Errors).IsEmpty();
        await Assert.That(_workOS.ByExternalId(clerkUserId)).IsNotNull();
    }

    [Test]
    public async Task The_command_exits_with_1_when_somebody_could_not_be_moved_or_the_export_cannot_be_read()
    {
        var command = new ImportClerkUsersCommand(NullLogger<ImportClerkUsersCommand>.Instance, Importer());
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, string.Join("\n", Header, Row(ClerkFbsApiFactory.NewUserId(), Email())));
            await Assert.That(await command.Import(path)).IsEqualTo(0);

            var taken = Email();
            _workOS.Add(taken, externalId: "user_somebody_else");
            await File.WriteAllTextAsync(path, string.Join("\n", Header, Row(ClerkFbsApiFactory.NewUserId(), taken)));
            await Assert.That(await command.Import(path)).IsEqualTo(1);

            await File.WriteAllTextAsync(path, "name,email\nSomebody,somebody@example.com");
            await Assert.That(await command.Import(path)).IsEqualTo(1);
            await Assert.That(await command.Import(path + ".missing")).IsEqualTo(1);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>Reading Clerk's export, which a spreadsheet may have been through.</summary>
public class ClerkExportTests
{
    [Test]
    public async Task Quoted_fields_with_commas_quotes_and_line_breaks_and_a_byte_order_mark_are_read()
    {
        var csv = "﻿id,first_name,last_name,primary_email_address,verified_email_addresses,password_digest,password_hasher\r\n"
            + "user_1,\"Lovelace, Ada\",\"The \"\"Countess\"\"\",ada@example.com,\"ada@example.com, ada2@example.com\",$2a$10$x,bcrypt\r\n"
            + "user_2,\"Two\nLines\",,b@example.com,b@example.com|c@example.com;d@example.com,,\r\n"
            + "\r\n";

        var users = ClerkExport.ReadCsv(csv);

        await Assert.That(users.Count).IsEqualTo(2);
        await Assert.That(users[0].FirstName).IsEqualTo("Lovelace, Ada");
        await Assert.That(users[0].LastName).IsEqualTo("The \"Countess\"");
        await Assert.That(users[0].VerifiedEmails).IsEquivalentTo(["ada@example.com", "ada2@example.com"]);
        await Assert.That(users[0].PasswordHasher).IsEqualTo("bcrypt");
        await Assert.That(users[0].HasTotp).IsFalse();
        await Assert.That(users[1].FirstName).IsEqualTo("Two\nLines");
        await Assert.That(users[1].LastName).IsNull();
        await Assert.That(users[1].VerifiedEmails).IsEquivalentTo(["b@example.com", "c@example.com", "d@example.com"]);
        await Assert.That(users[1].PasswordDigest).IsNull();
    }

    [Test]
    public async Task An_address_is_only_unverified_when_clerk_lists_it_as_unverified()
    {
        var users = ClerkExport.ReadCsv(
            "id,primary_email_address,verified_email_addresses,unverified_email_addresses\nuser_1,a@example.com,,\nuser_2,b@example.com,,B@example.com\nuser_3,c@example.com,c@example.com,"
        );

        await Assert.That(users.Select(u => u.PrimaryEmailUnverified)).IsEquivalentTo([false, true, false]);
    }

    [Test]
    public async Task What_is_not_clerks_export_is_refused()
    {
        await Assert.That(() => ClerkExport.ReadCsv("name,email\nA,a@example.com")).Throws<FormatException>();
        await Assert.That(() => ClerkExport.ReadCsv("")).Throws<FormatException>();
        await Assert.That(() => ClerkExport.ReadCsv("id,primary_email_address\nuser_1,\"unterminated")).Throws<FormatException>();
    }
}
