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
using ListTenantsCommand = Migrator::Fbs.DbMigrator.Commands.ListTenantsCommand;
using SuspendCommand = Migrator::Fbs.DbMigrator.Commands.SuspendCommand;

namespace Fbs.WebApi.Tests;

/// <summary>Whoever runs the system stopping an organisation being used, and letting it be again.</summary>
public class SuspensionTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private SuspendCommand Command() => new(NullLogger<SuspendCommand>.Instance, new TenantSuspensions(Factory.Db));

    private static object Booking(Guid facilityId) => new
    {
        conduct = "Lesson",
        slots = new[] { new { facilityId, startDateTime = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(2), endDateTime = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(3) } },
    };

    [Test]
    public async Task A_suspended_organisation_cannot_be_used_by_anybody_in_it_and_nothing_in_it_is_lost()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        var hall = org.AddFacility("Hall");
        var invite = await (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var token = invite.GetProperty("token").GetString()!;
        var booked = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Booking(hall));
        // A booking on a slot may not be on this one: what matters is that the count doesn't change
        var bookingsBefore = Factory.Db.Queryable<DataBooking>().Count(b => b.TenantId == org.TenantId);
        var membersBefore = Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId);

        await Assert.That(await Command().Suspend(org.Slug)).IsEqualTo(0);

        foreach (var client in new[] { org.Admin, member })
        {
            foreach (var route in new[] { $"/t/{org.Slug}", $"/t/{org.Slug}/Bookings", $"/t/{org.Slug}/Facilities/Bookable" })
            {
                var response = await client.GetAsync(route);
                await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
                await Assert.That(await response.Content.ReadAsStringAsync()).Contains("unavailable");
            }
        }

        await Assert.That(await org.Admin.GetAsync($"/t/{org.Slug}/Settings")).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Booking(hall))).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { })).HasStatus(HttpStatusCode.Forbidden);

        // Nobody new gets in with a link, and it looks as if the link was never there
        var newcomer = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        await Assert.That(await newcomer.GetAsync($"/Invites/{token}")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await newcomer.PostAsJsonAsync($"/Invites/{token}/Accept", new { })).HasStatus(HttpStatusCode.NotFound);

        // Others are not affected, and people can still say who they are, and are told the organisation is there
        await Assert.That(await other.Admin.GetAsync($"/t/{other.Slug}")).HasStatus(HttpStatusCode.OK);
        var me = await member.GetAsync("/Me");
        await Assert.That(me).HasStatus(HttpStatusCode.OK);
        await Assert.That(await me.Content.ReadAsStringAsync()).Contains(org.Slug);

        await Assert.That(Factory.Db.Queryable<DataBooking>().Count(b => b.TenantId == org.TenantId)).IsEqualTo(bookingsBefore);
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId)).IsEqualTo(membersBefore);
        _ = booked;
    }

    [Test]
    public async Task An_organisation_that_is_unsuspended_is_as_it_was()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        var hall = org.AddFacility("Hall");
        var invite = await (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var token = invite.GetProperty("token").GetString()!;

        await Command().Suspend(org.Slug);
        await Assert.That(await member.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await Command().Unsuspend(org.Slug)).IsEqualTo(0);

        await Assert.That(await member.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await org.Admin.GetAsync($"/t/{org.Slug}/Settings")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Booking(hall))).HasStatus(HttpStatusCode.Created);
        await Assert.That(await Factory.ClientFor(ClerkFbsApiFactory.NewUserId()).GetAsync($"/Invites/{token}")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task The_commands_say_what_happened_in_their_exit_codes_and_doing_it_twice_changes_nothing()
    {
        var org = await Factory.CreateOrgAsync();
        var command = Command();

        await Assert.That(await command.Suspend(org.Slug)).IsEqualTo(0);
        await Assert.That(await command.Suspend(org.Slug)).IsEqualTo(0);
        await Assert.That(Factory.Db.Queryable<Tenant>().First(t => t.Id == org.TenantId).Status).IsEqualTo(TenantStatus.Suspended);
        await Assert.That(await command.Unsuspend(org.Slug)).IsEqualTo(0);
        await Assert.That(await command.Unsuspend(org.Slug)).IsEqualTo(0);
        await Assert.That(Factory.Db.Queryable<Tenant>().First(t => t.Id == org.TenantId).Status).IsEqualTo(TenantStatus.Active);

        await Assert.That(await command.Suspend("no-such-org")).IsEqualTo(1);
        await Assert.That(await command.Unsuspend("no-such-org")).IsEqualTo(1);

        // What each was, which the exit code doesn't say
        var suspensions = new TenantSuspensions(Factory.Db);
        await Assert.That(await suspensions.SetAsync(org.Slug, true, CancellationToken.None)).IsEqualTo(SuspensionOutcome.Changed);
        await Assert.That(await suspensions.SetAsync(org.Slug, true, CancellationToken.None)).IsEqualTo(SuspensionOutcome.AlreadyThatWay);
        await Assert.That(await suspensions.SetAsync(org.Slug, false, CancellationToken.None)).IsEqualTo(SuspensionOutcome.Changed);
        await Assert.That(await suspensions.SetAsync(org.Slug, false, CancellationToken.None)).IsEqualTo(SuspensionOutcome.AlreadyThatWay);
        await Assert.That(await suspensions.SetAsync("no-such-org", true, CancellationToken.None)).IsEqualTo(SuspensionOutcome.NoSuchOrganization);
    }

    [Test]
    public async Task The_list_says_which_organisations_there_are_with_how_they_are_being_used()
    {
        var org = await Factory.CreateOrgAsync(orgName: "Listed Org");
        var quiet = await Factory.CreateOrgAsync();
        await org.AddMemberAsync();
        await org.AddMemberAsync(status: MemberStatus.Removed);
        var hall = org.AddFacility("Hall");
        Factory.Db.Insertable(new DataBooking { Id = Guid.NewGuid(), TenantId = org.TenantId, FacilityId = hall, StartUtc = DateTimeOffset.UtcNow.AddDays(3), EndUtc = DateTimeOffset.UtcNow.AddDays(3).AddHours(1), Conduct = "New", CreatedAt = DateTimeOffset.UtcNow })
            .ExecuteCommand();
        Factory.Db.Insertable(new DataBooking { Id = Guid.NewGuid(), TenantId = org.TenantId, FacilityId = hall, StartUtc = DateTimeOffset.UtcNow.AddDays(4), EndUtc = DateTimeOffset.UtcNow.AddDays(4).AddHours(1), Conduct = "Old", CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) })
            .ExecuteCommand();
        await Command().Suspend(quiet.Slug);

        var list = await new TenantSuspensions(Factory.Db).ListAsync(CancellationToken.None);

        var listed = list.Single(t => t.Slug == org.Slug);
        await Assert.That(listed.Name).IsEqualTo("Listed Org");
        await Assert.That(listed.Status).IsEqualTo(TenantStatus.Active);
        // The founder and one more: the person who was removed isn't counted
        await Assert.That(listed.People).IsEqualTo(2);
        await Assert.That(listed.BookingsInLastDay).IsEqualTo(1);
        await Assert.That(list.Single(t => t.Slug == quiet.Slug).Status).IsEqualTo(TenantStatus.Suspended);
        await Assert.That(await new ListTenantsCommand(NullLogger<ListTenantsCommand>.Instance, new TenantSuspensions(Factory.Db)).List()).IsEqualTo(0);
    }
}
