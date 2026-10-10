extern alias Migrator;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;
using AuditEntry = Fbs.WebApi.Data.Entities.AuditEntry;
using SuspendCommand = Migrator::Fbs.DbMigrator.Commands.SuspendCommand;

namespace Fbs.WebApi.Tests;

/// <summary>What admins do to an organisation being written down, and shown to them.</summary>
public class AuditTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static async Task<List<JsonElement>> AuditOf(HttpClient client, string slug, string query = "")
    {
        var response = await client.GetAsync($"/t/{slug}/Audit{query}");
        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    private static string[] Actions(IEnumerable<JsonElement> entries) => entries.Select(e => e.GetProperty("action").GetString()!).ToArray();

    private static string[] Summaries(IEnumerable<JsonElement> entries) => entries.Select(e => e.GetProperty("summary").GetString()!).ToArray();

    private static async Task<Guid> IdOf(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Test]
    public async Task What_an_admin_changes_is_written_down_with_who_did_it_the_latest_first()
    {
        var org = await Factory.CreateOrgAsync(founderName: "Founder Name");
        var slug = org.Slug;

        (await org.Admin.PutAsJsonAsync($"/t/{slug}/Settings", new { name = "Renamed Org", timeZone = "Asia/Singapore", defaultCountryCode = "65", slotMinutes = 15, requireApproval = true })).EnsureSuccessStatusCode();
        var unit = await IdOf(await org.Admin.PostAsJsonAsync($"/t/{slug}/Units", new { name = "Alpha" }));
        (await org.Admin.PutAsJsonAsync($"/t/{slug}/Units/{unit}", new { name = "Bravo" })).EnsureSuccessStatusCode();
        var facility = await IdOf(await org.Admin.PostAsJsonAsync($"/t/{slug}/Facilities", new { name = "Hall", availableToAll = true }));
        (await org.Admin.PutAsJsonAsync($"/t/{slug}/Facilities/{facility}", new { name = "Big Hall", availableToAll = true })).EnsureSuccessStatusCode();
        var invite = await IdOf(await org.Admin.PostAsJsonAsync($"/t/{slug}/Invites", new { role = "Admin", expiresInDays = 1, maxUses = 1 }));
        (await org.Admin.DeleteAsync($"/t/{slug}/Invites/{invite}")).EnsureSuccessStatusCode();
        (await org.Admin.DeleteAsync($"/t/{slug}/Facilities/{facility}")).EnsureSuccessStatusCode();
        (await org.Admin.DeleteAsync($"/t/{slug}/Units/{unit}")).EnsureSuccessStatusCode();

        var entries = await AuditOf(org.Admin, slug);

        await Assert.That(Actions(entries)).IsEquivalentTo(
            ["unit.deleted", "facility.deleted", "invite.stopped", "invite.created", "facility.changed", "facility.created", "unit.renamed", "unit.created", "settings.changed", "tenant.created"],
            CollectionOrdering.Matching
        );
        await Assert.That(Summaries(entries)).IsEquivalentTo(
            [
                "Deleted the unit Bravo.",
                "Deleted the facility Big Hall.",
                "Stopped an invite link.",
                "Made an invite link to join as an admin, for 1 day and up to 1 person.",
                "Changed the facility Hall, which is now Big Hall.",
                "Added the facility Hall.",
                "Renamed the unit Alpha to Bravo.",
                "Added the unit Alpha.",
                "Changed the settings: name, shortest booking.",
                "Made the organisation.",
            ],
            CollectionOrdering.Matching
        );
        await Assert.That(entries.All(e => e.GetProperty("actor").GetProperty("displayName").GetString() == "Founder Name")).IsTrue();
        await Assert.That(entries[0].GetProperty("targetType").GetString()).IsEqualTo("unit");
        await Assert.That(entries[0].GetProperty("targetId").GetGuid()).IsEqualTo(unit);
        var times = entries.Select(e => e.GetProperty("at").GetDateTimeOffset()).ToList();
        await Assert.That(times.SequenceEqual(times.OrderByDescending(t => t))).IsTrue();
    }

    [Test]
    public async Task Nothing_is_written_when_nothing_changed_or_the_change_was_refused()
    {
        var org = await Factory.CreateOrgAsync();
        var slug = org.Slug;
        var unit = await IdOf(await org.Admin.PostAsJsonAsync($"/t/{slug}/Units", new { name = "Alpha" }));
        var invite = await IdOf(await org.Admin.PostAsJsonAsync($"/t/{slug}/Invites", new { }));
        var hall = org.AddFacility("Hall");
        var (_, memberId) = await org.AddMemberAsync(unitId: unit);
        var before = (await AuditOf(org.Admin, slug)).Count;

        // The same settings, the same name, a name that is taken, a unit that is in use, the same link stopped twice
        (await org.Admin.PutAsJsonAsync($"/t/{slug}/Settings", new { name = "Test Org", timeZone = "Asia/Singapore", defaultCountryCode = "65", slotMinutes = 30, requireApproval = true })).EnsureSuccessStatusCode();
        (await org.Admin.PutAsJsonAsync($"/t/{slug}/Units/{unit}", new { name = "Alpha" })).EnsureSuccessStatusCode();
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{slug}/Units", new { name = "Alpha" })).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await org.Admin.DeleteAsync($"/t/{slug}/Units/{unit}")).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{slug}/Facilities", new { name = "Hall", availableToAll = true })).HasStatus(HttpStatusCode.Conflict);
        (await org.Admin.DeleteAsync($"/t/{slug}/Invites/{invite}")).EnsureSuccessStatusCode();
        (await org.Admin.DeleteAsync($"/t/{slug}/Invites/{invite}")).EnsureSuccessStatusCode();
        _ = hall;
        _ = memberId;

        var after = await AuditOf(org.Admin, slug);
        await Assert.That(after.Count).IsEqualTo(before + 1);
        await Assert.That(after[0].GetProperty("action").GetString()).IsEqualTo("invite.stopped");
    }

    [Test]
    public async Task Only_admins_can_read_it_and_an_organisation_sees_only_its_own()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Mine" });
        await other.Admin.PostAsJsonAsync($"/t/{other.Slug}/Units", new { name = "Theirs" });

        await Assert.That(await member.GetAsync($"/t/{org.Slug}/Audit")).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await other.Admin.GetAsync($"/t/{org.Slug}/Audit")).HasStatus(HttpStatusCode.NotFound);
        using var nobody = Factory.CreateClient();
        await Assert.That((await nobody.GetAsync($"/t/{org.Slug}/Audit")).StatusCode).IsNotEqualTo(HttpStatusCode.OK);

        await Assert.That(Summaries(await AuditOf(org.Admin, org.Slug))).IsEquivalentTo(["Added the unit Mine.", "Made the organisation."]);
        await Assert.That(Summaries(await AuditOf(other.Admin, other.Slug))).IsEquivalentTo(["Added the unit Theirs.", "Made the organisation."]);
    }

    [Test]
    public async Task It_is_asked_for_a_page_at_a_time_from_the_latest_back()
    {
        var org = await Factory.CreateOrgAsync();
        for (var i = 1; i <= 5; i++)
        {
            (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = $"Unit {i}" })).EnsureSuccessStatusCode();
        }

        var first = await AuditOf(org.Admin, org.Slug, "?limit=2");
        var second = await AuditOf(org.Admin, org.Slug, $"?limit=2&before={Uri.EscapeDataString(first[^1].GetProperty("at").GetString()!)}");
        var third = await AuditOf(org.Admin, org.Slug, $"?limit=2&before={Uri.EscapeDataString(second[^1].GetProperty("at").GetString()!)}");

        await Assert.That(Summaries(first)).IsEquivalentTo(["Added the unit Unit 5.", "Added the unit Unit 4."], CollectionOrdering.Matching);
        await Assert.That(Summaries(second)).IsEquivalentTo(["Added the unit Unit 3.", "Added the unit Unit 2."], CollectionOrdering.Matching);
        await Assert.That(Summaries(third)).IsEquivalentTo(["Added the unit Unit 1.", "Made the organisation."], CollectionOrdering.Matching);
        await Assert.That((await AuditOf(org.Admin, org.Slug, "?limit=0")).Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_page_is_never_more_than_200_however_many_are_asked_for()
    {
        var org = await Factory.CreateOrgAsync();
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        Factory.Db.Insertable(
                Enumerable
                    .Range(0, 210)
                    .Select(i => new AuditEntry
                    {
                        Id = Guid.NewGuid(),
                        TenantId = org.TenantId,
                        Action = "unit.created",
                        Summary = $"Added the unit {i}.",
                        At = start.AddSeconds(i),
                    })
                    .ToList()
            )
            .ExecuteCommand();

        await Assert.That((await AuditOf(org.Admin, org.Slug, "?limit=100000")).Count).IsEqualTo(200);
        await Assert.That((await AuditOf(org.Admin, org.Slug)).Count).IsEqualTo(50);
    }

    [Test]
    public async Task Whoever_runs_the_system_suspending_it_is_written_down_without_an_actor()
    {
        var org = await Factory.CreateOrgAsync();
        var command = new SuspendCommand(NullLogger<SuspendCommand>.Instance, new TenantSuspensions(Factory.Db));

        await command.Suspend(org.Slug);
        await command.Suspend(org.Slug);
        await command.Unsuspend(org.Slug);

        var entries = await AuditOf(org.Admin, org.Slug);
        await Assert.That(Actions(entries)).IsEquivalentTo(["tenant.unsuspended", "tenant.suspended", "tenant.created"], CollectionOrdering.Matching);
        // The organisation was made by its founder, and suspended by nobody in it
        await Assert.That(entries.Take(2).All(e => e.GetProperty("actor").ValueKind == JsonValueKind.Null)).IsTrue();
    }

    [Test]
    public async Task When_somebodys_account_is_erased_what_they_did_stays_and_they_are_a_former_member()
    {
        var org = await Factory.CreateOrgAsync(founderName: "Leaving Founder");
        var (other, _) = await org.AddMemberAsync("Staying Admin", MemberRole.Admin);
        (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Alpha" })).EnsureSuccessStatusCode();
        // What is written down has no name of a person in it: it is worked out when it is read
        await Assert.That(Factory.Db.Queryable<AuditEntry>().Where(e => e.TenantId == org.TenantId).ToList().Any(e => e.Summary.Contains("Leaving", StringComparison.OrdinalIgnoreCase))).IsFalse();

        var accountId = await Factory.AccountIdOfAsync(org.Admin);
        var clerkUserId = Factory.Db.Queryable<UserAccount>().First(a => a.Id == accountId).ClerkUserId;
        await new AccountErasure(Factory.Db, NullLogger<AccountErasure>.Instance).EraseClerkUserAsync(clerkUserId!, CancellationToken.None);

        var entries = await AuditOf(other, org.Slug);
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.All(e => e.GetProperty("actor").GetProperty("displayName").GetString() == "Former member")).IsTrue();
        await Assert.That(entries[0].GetProperty("summary").GetString()).IsEqualTo("Added the unit Alpha.");
    }
}
