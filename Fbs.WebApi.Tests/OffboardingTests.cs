extern alias Migrator;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using SuspendCommand = Migrator::Fbs.DbMigrator.Commands.SuspendCommand;

namespace Fbs.WebApi.Tests;

/// <summary>An admin deleting their organisation: asked for, unusable from then, and taken back while it is still there.</summary>
public class OffboardingTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private Task<HttpResponseMessage> DeleteAsync(HttpClient client, string slug, string? confirm) => client.PostAsJsonAsync($"/Tenants/{slug}/Deletion", new { confirm });

    private Tenant Stored(TestOrg org) => Factory.Db.Queryable<Tenant>().First(t => t.Id == org.TenantId);

    private static object Booking(Guid facilityId) => new
    {
        conduct = "Lesson",
        slots = new[] { new { facilityId, startDateTime = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(2), endDateTime = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(3) } },
    };

    [Test]
    public async Task An_admin_asks_for_it_to_be_deleted_and_nobody_can_use_it_from_then_but_nothing_is_lost()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        var (admin2, _) = await org.AddMemberAsync("Second Admin", MemberRole.Admin);
        var hall = org.AddFacility("Hall");
        (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Booking(hall))).EnsureSuccessStatusCode();
        var invite = await (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var token = invite.GetProperty("token").GetString()!;
        var bookings = Factory.Db.Queryable<DataBooking>().Count(b => b.TenantId == org.TenantId);
        var members = Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId);

        var asked = await DeleteAsync(org.Admin, org.Slug, org.Slug);

        await Assert.That(asked).HasStatus(HttpStatusCode.OK);
        var deleteAfter = (await asked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deleteAfter").GetDateTimeOffset();
        await Assert.That(deleteAfter).IsBetween(DateTimeOffset.UtcNow.AddDays(29.9), DateTimeOffset.UtcNow.AddDays(30.1));
        await Assert.That(Stored(org).Status).IsEqualTo(TenantStatus.PendingDeletion);
        await Assert.That(Stored(org).DeleteAfter!.Value).IsEqualTo(deleteAfter).Within(TimeSpan.FromSeconds(1));

        // Nobody in it can use it, and they are told why
        foreach (var client in new[] { org.Admin, member, admin2 })
        {
            var response = await client.GetAsync($"/t/{org.Slug}");
            await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
            await Assert.That(await response.Content.ReadAsStringAsync()).Contains("pending-deletion");
        }

        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Booking(hall))).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await org.Admin.GetAsync($"/t/{org.Slug}/Settings")).HasStatus(HttpStatusCode.Forbidden);
        var newcomer = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        await Assert.That(await newcomer.GetAsync($"/Invites/{token}")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await newcomer.PostAsJsonAsync($"/Invites/{token}/Accept", new { })).HasStatus(HttpStatusCode.NotFound);

        // They are still told they are in it, and that it is to be deleted, and when, which is how they find out that it can be restored
        var me = (await member.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("memberships").EnumerateArray().Single(m => m.GetProperty("tenantSlug").GetString() == org.Slug);
        await Assert.That(me.GetProperty("tenantStatus").GetString()).IsEqualTo("PendingDeletion");
        await Assert.That(me.GetProperty("deleteAfter").GetDateTimeOffset()).IsEqualTo(deleteAfter).Within(TimeSpan.FromSeconds(1));

        // Nothing is deleted, and it is written down
        await Assert.That(Factory.Db.Queryable<DataBooking>().Count(b => b.TenantId == org.TenantId)).IsEqualTo(bookings);
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId)).IsEqualTo(members);
        var written = Factory.Db.Queryable<AuditEntry>().Where(e => e.TenantId == org.TenantId && e.Action == "tenant.deletion_requested").ToList().Single();
        await Assert.That(written.ActorMemberId).IsNotNull();
    }

    [Test]
    public async Task It_has_to_be_asked_for_by_typing_its_address_and_only_by_an_admin_of_it()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        var (waiting, _) = await org.AddMemberAsync("Waiting Admin", MemberRole.Admin, MemberStatus.Pending);
        var (removed, _) = await org.AddMemberAsync("Removed Admin", MemberRole.Admin, MemberStatus.Removed);
        var stranger = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        var other = await Factory.CreateOrgAsync();

        await Assert.That(await DeleteAsync(org.Admin, org.Slug, null)).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await DeleteAsync(org.Admin, org.Slug, "")).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await DeleteAsync(org.Admin, org.Slug, "something-else")).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await DeleteAsync(org.Admin, org.Slug, org.Slug.ToUpperInvariant())).HasStatus(HttpStatusCode.BadRequest);

        // Told there is no such organisation, as for one that isn't there
        foreach (var client in new[] { member, waiting, removed, stranger, other.Admin })
        {
            await Assert.That(await DeleteAsync(client, org.Slug, org.Slug)).HasStatus(HttpStatusCode.NotFound);
        }

        await Assert.That(await DeleteAsync(org.Admin, "no-such-organisation", "no-such-organisation")).HasStatus(HttpStatusCode.NotFound);
        using var nobody = Factory.CreateClient();
        await Assert.That((await DeleteAsync(nobody, org.Slug, org.Slug)).StatusCode).IsNotEqualTo(HttpStatusCode.OK);
        await Assert.That(Stored(org).Status).IsEqualTo(TenantStatus.Active);
        await Assert.That(Stored(org).DeleteAfter).IsNull();
        await Assert.That(await org.Admin.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Any_admin_of_it_can_restore_it_until_it_is_deleted_for_good_and_it_is_as_it_was()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        var (admin2, _) = await org.AddMemberAsync("Second Admin", MemberRole.Admin);
        var hall = org.AddFacility("Hall");
        (await DeleteAsync(org.Admin, org.Slug, org.Slug)).EnsureSuccessStatusCode();

        // Somebody who is not an admin can't, and it isn't found for a stranger
        await Assert.That(await member.DeleteAsync($"/Tenants/{org.Slug}/Deletion")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await Factory.ClientFor(ClerkFbsApiFactory.NewUserId()).DeleteAsync($"/Tenants/{org.Slug}/Deletion")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(Stored(org).Status).IsEqualTo(TenantStatus.PendingDeletion);

        // Even after the time is up, until it has been purged
        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { DeleteAfter = DateTimeOffset.UtcNow.AddDays(-1) }).Where(t => t.Id == org.TenantId).ExecuteCommand();
        await Assert.That(await admin2.DeleteAsync($"/Tenants/{org.Slug}/Deletion")).HasStatus(HttpStatusCode.NoContent);

        await Assert.That(Stored(org).Status).IsEqualTo(TenantStatus.Active);
        await Assert.That(Stored(org).DeleteAfter).IsNull();
        await Assert.That(await member.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Booking(hall))).HasStatus(HttpStatusCode.Created);
        var me = (await member.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("memberships").EnumerateArray().Single(m => m.GetProperty("tenantSlug").GetString() == org.Slug);
        await Assert.That(me.GetProperty("tenantStatus").GetString()).IsEqualTo("Active");
        await Assert.That(me.GetProperty("deleteAfter").ValueKind).IsEqualTo(JsonValueKind.Null);

        // Written down, by who, and seen by the admins
        var history = (await (await org.Admin.GetAsync($"/t/{org.Slug}/Audit")).Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        await Assert.That(history).Contains("tenant.restored");
        await Assert.That(history).Contains("tenant.deletion_requested");

        // It isn't to be deleted now, so there is nothing to restore
        await Assert.That(await org.Admin.DeleteAsync($"/Tenants/{org.Slug}/Deletion")).HasStatus(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task What_is_to_be_deleted_or_suspended_cannot_be_asked_to_be_deleted_and_is_not_suspended_by_whoever_runs_the_system()
    {
        var org = await Factory.CreateOrgAsync();
        var suspended = await Factory.CreateOrgAsync();
        var command = new SuspendCommand(NullLogger<SuspendCommand>.Instance, new TenantSuspensions(Factory.Db));
        await command.Suspend(suspended.Slug);

        await Assert.That(await DeleteAsync(suspended.Admin, suspended.Slug, suspended.Slug)).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(Stored(suspended).Status).IsEqualTo(TenantStatus.Suspended);

        (await DeleteAsync(org.Admin, org.Slug, org.Slug)).EnsureSuccessStatusCode();
        var again = await DeleteAsync(org.Admin, org.Slug, org.Slug);
        await Assert.That(again).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(Factory.Db.Queryable<AuditEntry>().Count(e => e.TenantId == org.TenantId && e.Action == "tenant.deletion_requested")).IsEqualTo(1);

        // It is more than suspended, and is left as it is, with the time it has
        var before = Stored(org).DeleteAfter;
        await Assert.That(await command.Suspend(org.Slug)).IsEqualTo(1);
        await Assert.That(await command.Unsuspend(org.Slug)).IsEqualTo(1);
        await Assert.That(Stored(org).Status).IsEqualTo(TenantStatus.PendingDeletion);
        await Assert.That(Stored(org).DeleteAfter).IsEqualTo(before);
    }
}
