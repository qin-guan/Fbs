extern alias Migrator;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using PurgeTenantsCommand = Migrator::Fbs.DbMigrator.Commands.PurgeTenantsCommand;

namespace Fbs.WebApi.Tests;

/// <summary>An organisation that was asked to be deleted being deleted for good, with everything of it and nothing of anybody else's.</summary>
public class PurgeTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private TenantPurges Purges() => new(Factory.Db);

    private PurgeTenantsCommand Command() => new(NullLogger<PurgeTenantsCommand>.Instance, Purges());

    private static object Booking(Guid facilityId, int day) => new
    {
        conduct = "Lesson",
        slots = new[] { new { facilityId, startDateTime = DateTimeOffset.UtcNow.Date.AddDays(day).AddHours(2), endDateTime = DateTimeOffset.UtcNow.Date.AddDays(day).AddHours(3) } },
    };

    /// <summary>An organisation with one of everything that belongs to one, made mostly the way it would be.</summary>
    private async Task<TestOrg> FilledOrgAsync(string name)
    {
        var org = await Factory.CreateOrgAsync(orgName: name);
        var unit = org.AddUnit("Alpha");
        var hall = org.AddFacility("Hall", availableToAll: false, unit);
        await org.AddMemberAsync("Member", unitId: unit);
        await org.AddMemberAsync("Waiting", status: MemberStatus.Pending);
        await org.AddMemberAsync("Removed", status: MemberStatus.Removed);
        (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Booking(hall, 3))).EnsureSuccessStatusCode();
        (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Booking(hall, 4))).EnsureSuccessStatusCode();
        (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { })).EnsureSuccessStatusCode();
        var bookingId = Factory.Db.Queryable<Booking>().First(b => b.TenantId == org.TenantId).Id;
        Factory.Db.Insertable(new BookingCalendarEvent { BookingId = bookingId, TenantId = org.TenantId, CalendarId = "cal", EventId = "event" }).ExecuteCommand();
        Factory.Db.Insertable(new CalendarConnection { Id = Guid.NewGuid(), TenantId = org.TenantId, CalendarId = "cal" }).ExecuteCommand();
        Factory.Db.Insertable(new RosterEntry { Id = Guid.NewGuid(), TenantId = org.TenantId, Name = "Rostered", Phone = "+6590000009" }).ExecuteCommand();
        Factory.Db.Insertable(new MemberClaimToken { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TenantId = org.TenantId, TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }).ExecuteCommand();
        return org;
    }

    /// <summary>How many of each thing belongs to the organisation.</summary>
    private Dictionary<string, int> Owned(TestOrg org) =>
        TenantPurges
            .TenantOwned.ToDictionary(
                type => type.Name,
                type => Factory.Db.Ado.GetInt($"SELECT COUNT(*) FROM `{Factory.Db.EntityMaintenance.GetEntityInfo(type).DbTableName}` WHERE `TenantId` = @id", new SqlSugar.SugarParameter("@id", org.TenantId))
            );

    private async Task AskAsync(TestOrg org, bool due)
    {
        (await org.Admin.PostAsJsonAsync($"/Tenants/{org.Slug}/Deletion", new { confirm = org.Slug })).EnsureSuccessStatusCode();
        if (due)
        {
            Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { DeleteAfter = DateTimeOffset.UtcNow.AddMinutes(-1) }).Where(t => t.Id == org.TenantId).ExecuteCommand();
        }
    }

    private bool Exists(TestOrg org) => Factory.Db.Queryable<Tenant>().Any(t => t.Id == org.TenantId);

    [Test]
    public async Task Everything_with_a_tenant_id_is_deleted_with_the_organisation()
    {
        var owned = TenantPurges.TenantOwned.ToHashSet();
        var withTenantId = SchemaDifferenceInspector.GetEntityTypes().Where(t => t.GetProperty("TenantId")?.PropertyType == typeof(Guid)).ToList();

        await Assert.That(withTenantId.Where(t => !owned.Contains(t)).Select(t => t.Name)).IsEmpty();
        await Assert.That(owned.Where(t => !withTenantId.Contains(t)).Select(t => t.Name)).IsEmpty();
    }

    [Test]
    public async Task An_organisation_that_is_due_is_deleted_with_everything_of_it_and_nothing_of_anybody_elses()
    {
        var doomed = await FilledOrgAsync("Doomed");
        var kept = await FilledOrgAsync("Kept");
        var pendingNotDue = await FilledOrgAsync("Not Yet");
        var founderAccount = await Factory.AccountIdOfAsync(doomed.Admin);
        var keptBefore = Owned(kept);

        // Every kind of thing is there to be deleted, or this proves nothing
        var before = Owned(doomed);
        await Assert.That(before.Where(o => o.Value == 0).Select(o => o.Key)).IsEmpty();
        await AskAsync(doomed, due: true);
        await AskAsync(pendingNotDue, due: false);
        var notDueBefore = Owned(pendingNotDue);

        var due = await Purges().DueAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        await Assert.That(due).Contains(doomed.Slug);
        await Assert.That(due).DoesNotContain(pendingNotDue.Slug);
        await Assert.That(due).DoesNotContain(kept.Slug);
        await Assert.That(await Command().Purge()).IsEqualTo(0);

        await Assert.That(Exists(doomed)).IsFalse();
        await Assert.That(Owned(doomed).Where(o => o.Value != 0).Select(o => o.Key)).IsEmpty();
        // Nothing else was touched
        await Assert.That(Exists(kept)).IsTrue();
        await Assert.That(Exists(pendingNotDue)).IsTrue();
        await Assert.That(Owned(kept)).IsEquivalentTo(keptBefore);
        await Assert.That(Owned(pendingNotDue)).IsEquivalentTo(notDueBefore);
        // People stay, and are told there is no such organisation
        await Assert.That(Factory.Db.Queryable<UserAccount>().Any(a => a.Id == founderAccount)).IsTrue();
        await Assert.That(await doomed.Admin.GetAsync($"/t/{doomed.Slug}")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await kept.Admin.GetAsync($"/t/{kept.Slug}")).HasStatus(HttpStatusCode.OK);
        var me = await doomed.Admin.GetFromJsonAsync<JsonElement>("/Me");
        await Assert.That(me.GetProperty("memberships").EnumerateArray().Any(m => m.GetProperty("tenantSlug").GetString() == doomed.Slug)).IsFalse();
    }

    [Test]
    public async Task What_is_not_to_be_deleted_or_not_due_is_left_and_a_dry_run_deletes_nothing()
    {
        var active = await FilledOrgAsync("Active");
        var suspended = await FilledOrgAsync("Suspended");
        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { Status = TenantStatus.Suspended }).Where(t => t.Id == suspended.TenantId).ExecuteCommand();
        var notYet = await FilledOrgAsync("Not Yet");
        await AskAsync(notYet, due: false);
        var dry = await FilledOrgAsync("Dry");
        await AskAsync(dry, due: true);
        var restored = await FilledOrgAsync("Restored");
        await AskAsync(restored, due: true);
        (await restored.Admin.DeleteAsync($"/Tenants/{restored.Slug}/Deletion")).EnsureSuccessStatusCode();

        foreach (var org in new[] { active, suspended, notYet, restored })
        {
            var result = await Purges().PurgeAsync(org.Slug, DateTimeOffset.UtcNow, early: false, dryRun: false, CancellationToken.None);
            await Assert.That(result.Outcome).IsEqualTo(PurgeOutcome.NotDue);
            await Assert.That(Exists(org)).IsTrue();
            await Assert.That(await Command().Purge(org.Slug)).IsEqualTo(1);
        }

        await Assert.That(await Command().Purge("no-such-organisation")).IsEqualTo(1);

        var before = Owned(dry);
        var said = await Purges().PurgeAsync(dry.Slug, DateTimeOffset.UtcNow, early: false, dryRun: true, CancellationToken.None);
        await Assert.That(said.Outcome).IsEqualTo(PurgeOutcome.Purged);
        await Assert.That(said.Deleted[nameof(Booking)]).IsEqualTo(before[nameof(Booking)]);
        await Assert.That(said.Deleted[nameof(TenantMember)]).IsEqualTo(before[nameof(TenantMember)]);
        await Assert.That(said.Deleted[nameof(Tenant)]).IsEqualTo(1);
        await Assert.That(await Command().Purge(dryRun: true)).IsEqualTo(0);
        await Assert.That(Exists(dry)).IsTrue();
        await Assert.That(Owned(dry)).IsEquivalentTo(before);
    }

    [Test]
    public async Task One_that_is_asked_to_be_deleted_can_be_deleted_before_it_is_due_only_by_name_and_only_if_asked()
    {
        var org = await FilledOrgAsync("Early");
        var notAsked = await FilledOrgAsync("Not Asked");
        await AskAsync(org, due: false);

        await Assert.That(await Command().Purge(org.Slug)).IsEqualTo(1);
        await Assert.That(Exists(org)).IsTrue();
        // Not without saying which
        await Assert.That(await Command().Purge(early: true)).IsEqualTo(1);
        await Assert.That(Exists(org)).IsTrue();
        // What an admin has not asked to be deleted is not, whatever is said
        await Assert.That(await Command().Purge(notAsked.Slug, early: true)).IsEqualTo(1);
        await Assert.That(Exists(notAsked)).IsTrue();

        await Assert.That(await Command().Purge(org.Slug, early: true)).IsEqualTo(0);

        await Assert.That(Exists(org)).IsFalse();
        await Assert.That(Owned(org).Where(o => o.Value != 0).Select(o => o.Key)).IsEmpty();
    }

    [Test]
    public async Task Its_address_can_be_used_again_once_it_is_gone_and_nobody_from_before_is_in_it()
    {
        var org = await FilledOrgAsync("Old");
        await AskAsync(org, due: true);
        await Assert.That(await Command().Purge()).IsEqualTo(0);

        // Taken until then, and free after
        var again = await org.Admin.PostAsJsonAsync("/Tenants", new { name = "New Org", slug = org.Slug, timeZone = "Asia/Singapore" });
        await Assert.That(again).HasStatus(HttpStatusCode.Created);
        var newOrg = Factory.Db.Queryable<Tenant>().First(t => t.Slug == org.Slug);
        await Assert.That(newOrg.Id).IsNotEqualTo(org.TenantId);
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == newOrg.Id)).IsEqualTo(1);
        await Assert.That(Factory.Db.Queryable<Booking>().Count(b => b.TenantId == newOrg.Id)).IsEqualTo(0);
    }
}
