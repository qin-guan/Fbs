using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

/// <summary>The members of an organisation, who they are, what they can do, and who is let in.</summary>
public class OrgMembersTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private TenantMember Stored(Guid id) => Factory.Db.Queryable<TenantMember>().First(m => m.Id == id);

    private static object Body(string name = "Someone", string? phone = null, Guid? unitId = null, string role = "Member", string scope = "None", string membership = "In") =>
        new { displayName = name, phone, unitId, role, notificationScope = scope, membership };

    [Test]
    public async Task An_admin_adds_someone_by_phone_number_who_belongs_once_they_sign_in()
    {
        var org = await Factory.CreateOrgAsync();
        var unit = org.AddUnit("Alpha");

        var response = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body(" CPT Booker ", "9123 4567", unit, "Member", "Unit"));

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        var member = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(member.GetProperty("displayName").GetString()).IsEqualTo("CPT Booker");
        await Assert.That(member.GetProperty("phone").GetString()).IsEqualTo("+6591234567");
        await Assert.That(member.GetProperty("status").GetString()).IsEqualTo("Unclaimed");
        await Assert.That(member.GetProperty("hasAccount").GetBoolean()).IsFalse();
        var stored = Stored(member.GetProperty("id").GetGuid());
        await Assert.That(stored.UnitId).IsEqualTo(unit);
        await Assert.That(stored.NotificationScope).IsEqualTo(NotificationScope.Unit);
        await Assert.That(stored.UserId).IsNull();
    }

    [Test]
    [Arguments("91234567", "+6591234567")]
    [Arguments("+65 9123-4567", "+6591234567")]
    [Arguments("0065 9123 4567", "+6591234567")]
    [Arguments("+44 20 7946 0958", "+442079460958")]
    public async Task A_phone_number_is_kept_in_one_form_whichever_way_it_was_written(string typed, string stored)
    {
        var org = await Factory.CreateOrgAsync();

        var response = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body(phone: typed));

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        await Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("phone").GetString()).IsEqualTo(stored);
    }

    [Test]
    public async Task What_is_not_a_phone_number_or_has_no_name_is_refused()
    {
        var org = await Factory.CreateOrgAsync();

        foreach (var phone in new[] { "", "abc", "12", "+65 (9123", "65+91234567", "9123 4567 8901 2345 678" })
        {
            await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body(phone: phone))).HasStatus(HttpStatusCode.BadRequest);
        }

        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body(name: " ", phone: "91234567"))).HasStatus(HttpStatusCode.BadRequest);
        // Only the admin who made the organisation is in it
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task The_same_number_written_another_way_is_a_conflict_even_if_they_have_left()
    {
        var org = await Factory.CreateOrgAsync();
        var first = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body("First", "91234567"));
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var again = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body("Second", "+65 9123 4567"));
        (await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{id}", Body("First", "91234567", membership: "Removed"))).EnsureSuccessStatusCode();
        var afterLeaving = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body("Second", "91234567"));

        await Assert.That(again).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await again.Content.ReadAsStringAsync()).Contains("phone-taken");
        await Assert.That(afterLeaving).HasStatus(HttpStatusCode.Conflict);
        // Another organisation can have them
        var other = await Factory.CreateOrgAsync();
        await Assert.That(await other.Admin.PostAsJsonAsync($"/t/{other.Slug}/Members", Body("Second", "91234567"))).HasStatus(HttpStatusCode.Created);
    }

    [Test]
    public async Task Admins_see_everyone_who_belongs_and_those_who_have_left_only_when_asked()
    {
        var org = await Factory.CreateOrgAsync("Founder");
        await org.AddMemberAsync("Active");
        await org.AddMemberAsync("Waiting", status: MemberStatus.Pending);
        await org.AddMemberAsync("Gone", status: MemberStatus.Removed);
        await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body("Unclaimed", "91234567"));

        var current = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Members");
        var all = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Members?includeRemoved=true");

        await Assert.That(current.EnumerateArray().Select(m => m.GetProperty("displayName").GetString())).IsEquivalentTo(["Active", "Founder", "Unclaimed", "Waiting"]);
        await Assert.That(all.GetArrayLength()).IsEqualTo(5);
    }

    [Test]
    public async Task Someone_waiting_is_let_in_or_turned_away_and_someone_removed_can_be_let_back()
    {
        var org = await Factory.CreateOrgAsync();
        var (waiting, waitingId) = await org.AddMemberAsync("Waiting", status: MemberStatus.Pending);
        var (turnedAway, turnedAwayId) = await org.AddMemberAsync("Turned away", status: MemberStatus.Pending);
        await Assert.That(await waiting.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.Forbidden);

        var approve = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{waitingId}", Body("Waiting"));
        var reject = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{turnedAwayId}", Body("Turned away", membership: "Removed"));

        await Assert.That(approve).HasStatus(HttpStatusCode.OK);
        await Assert.That((await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()).IsEqualTo("Active");
        await Assert.That(await waiting.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.OK);
        await Assert.That(reject).HasStatus(HttpStatusCode.OK);
        await Assert.That(await turnedAway.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.NotFound);

        var back = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{turnedAwayId}", Body("Turned away"));
        await Assert.That((await back.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()).IsEqualTo("Active");
        await Assert.That(await turnedAway.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Someone_who_has_not_signed_in_stays_unclaimed_when_let_back_in_and_is_found_by_their_number()
    {
        var org = await Factory.CreateOrgAsync();
        var added = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body("Carried over", "91234567"));
        var id = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{id}", Body("Carried over", "91234567", membership: "Removed"))).EnsureSuccessStatusCode();

        var back = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{id}", Body("Carried over", "91234567"));
        var noNumber = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{id}", Body("Carried over", null));

        await Assert.That((await back.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()).IsEqualTo("Unclaimed");
        await Assert.That(noNumber).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await noNumber.Content.ReadAsStringAsync()).Contains("phone-needed");
        await Assert.That(Stored(id).Phone).IsEqualTo("+6591234567");
    }

    [Test]
    public async Task An_admin_changes_who_a_member_is_where_they_are_and_what_they_hear_about()
    {
        var org = await Factory.CreateOrgAsync();
        var unit = org.AddUnit("Alpha");
        var (_, id) = await org.AddMemberAsync("Before");

        var response = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{id}", Body("After", "+65 8123 4567", unit, "Admin", "All"));

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var stored = Stored(id);
        await Assert.That(stored.DisplayName).IsEqualTo("After");
        await Assert.That(stored.Phone).IsEqualTo("+6581234567");
        await Assert.That(stored.UnitId).IsEqualTo(unit);
        await Assert.That(stored.Role).IsEqualTo(MemberRole.Admin);
        await Assert.That(stored.NotificationScope).IsEqualTo(NotificationScope.All);
        await Assert.That(stored.Status).IsEqualTo(MemberStatus.Active);
        await Assert.That(stored.UserId).IsNotNull();
    }

    [Test]
    public async Task The_last_admin_cannot_be_made_a_member_or_removed_but_one_of_two_can()
    {
        var org = await Factory.CreateOrgAsync("Founder");
        var founderId = Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId).Id;

        var demote = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{founderId}", Body("Founder", role: "Member"));
        var leave = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{founderId}", Body("Founder", role: "Admin", membership: "Removed"));

        await Assert.That(demote).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await demote.Content.ReadAsStringAsync()).Contains("last-admin");
        await Assert.That(leave).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(Stored(founderId).Role).IsEqualTo(MemberRole.Admin);
        await Assert.That(Stored(founderId).Status).IsEqualTo(MemberStatus.Active);

        var (second, secondId) = await org.AddMemberAsync("Second", MemberRole.Admin);
        await Assert.That(await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{founderId}", Body("Founder", role: "Member"))).HasStatus(HttpStatusCode.OK);

        // Now they are not an admin any more, and the one who is can't follow them
        await Assert.That(await org.Admin.GetAsync($"/t/{org.Slug}/Members")).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await second.PutAsJsonAsync($"/t/{org.Slug}/Members/{secondId}", Body("Second", role: "Member"))).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await second.PutAsJsonAsync($"/t/{org.Slug}/Members/{secondId}", Body("Second again", role: "Admin"))).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Two_admins_removing_each_other_at_the_same_moment_leave_one()
    {
        // Each sees the other as an admin who is still there, unless whoever comes second waits for the first
        for (var round = 0; round < 10; round++)
        {
            var org = await Factory.CreateOrgAsync("First");
            var (second, secondId) = await org.AddMemberAsync("Second", MemberRole.Admin);
            var firstId = Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId && m.DisplayName == "First").Id;

            List<Task<HttpResponseMessage>> requests;
            using (ExecutionContext.SuppressFlow())
            {
                requests =
                [
                    Task.Run(() => org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{secondId}", Body("Second", role: "Admin", membership: "Removed"))),
                    Task.Run(() => second.PutAsJsonAsync($"/t/{org.Slug}/Members/{firstId}", Body("First", role: "Admin", membership: "Removed"))),
                ];
            }

            var statuses = (await Task.WhenAll(requests)).Select(r => r.StatusCode).ToList();

            await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId && m.Role == MemberRole.Admin && m.Status == MemberStatus.Active)).IsEqualTo(1);
            await Assert.That(statuses.Count(s => s == HttpStatusCode.OK)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_unit_of_another_organisation_or_a_member_of_one_is_not_reachable()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var theirUnit = other.AddUnit("Theirs");
        var (_, mine) = await org.AddMemberAsync("Mine");
        var (_, theirs) = await other.AddMemberAsync("Theirs");

        var addWithUnit = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", Body("New", "91234567", theirUnit));
        var moveToUnit = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{mine}", Body("Mine", unitId: theirUnit));
        var changeTheirs = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{theirs}", Body("Taken over", role: "Admin"));
        var listed = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Members");

        await Assert.That(addWithUnit).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(moveToUnit).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await moveToUnit.Content.ReadAsStringAsync()).Contains("unit-unknown");
        await Assert.That(changeTheirs).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(Stored(theirs).DisplayName).IsEqualTo("Theirs");
        await Assert.That(Stored(mine).UnitId).IsNull();
        await Assert.That(listed.EnumerateArray().Select(m => m.GetProperty("displayName").GetString())).IsEquivalentTo(["Founder", "Mine"]);
    }

    [Test]
    public async Task Only_admins_manage_members()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, memberId) = await org.AddMemberAsync();

        var responses = new[]
        {
            await member.GetAsync($"/t/{org.Slug}/Members"),
            await member.PostAsJsonAsync($"/t/{org.Slug}/Members", Body(phone: "91234567")),
            // Including making themselves an admin
            await member.PutAsJsonAsync($"/t/{org.Slug}/Members/{memberId}", Body("A Member", role: "Admin")),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.Forbidden]);
        await Assert.That(Stored(memberId).Role).IsEqualTo(MemberRole.Member);
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId)).IsEqualTo(2);
    }
}
