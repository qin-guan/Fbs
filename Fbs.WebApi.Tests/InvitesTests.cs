using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

/// <summary>Links that admins share for people to join with, and joining with one.</summary>
public class InvitesTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static async Task<(Guid Id, string Token)> InviteAsync(HttpClient admin, string slug, object? body = null)
    {
        var response = await admin.PostAsJsonAsync($"/t/{slug}/Invites", body ?? new { });
        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (invite.GetProperty("id").GetGuid(), invite.GetProperty("token").GetString()!);
    }

    private HttpClient NewPerson(string? name = "New Person") => Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), name);

    private TenantInvite Stored(Guid id) => Factory.Db.Queryable<TenantInvite>().First(i => i.Id == id);

    private Task SetRequireApprovalAsync(TestOrg org, bool requireApproval) =>
        org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Settings", new { name = "Test Org", timeZone = "Asia/Singapore", defaultCountryCode = "65", slotMinutes = 30, requireApproval });

    [Test]
    public async Task An_admin_makes_a_link_which_is_shown_once_and_only_a_hash_of_it_is_kept()
    {
        var org = await Factory.CreateOrgAsync();
        var unit = org.AddUnit("Alpha");

        var response = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { unitId = unit, expiresInDays = 3, maxUses = 5 });

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        var made = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = made.GetProperty("token").GetString()!;
        await Assert.That(token.Length).IsGreaterThanOrEqualTo(43);
        await Assert.That(token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')).IsTrue();
        var stored = Stored(made.GetProperty("id").GetGuid());
        await Assert.That(stored.TokenHash).IsNotEqualTo(token);
        await Assert.That(stored.TokenHash.Length).IsEqualTo(64);
        await Assert.That(stored.UnitId).IsEqualTo(unit);
        await Assert.That(stored.MaxUses).IsEqualTo(5);
        await Assert.That(stored.ExpiresAt).IsGreaterThan(DateTimeOffset.UtcNow.AddDays(2.9));
        await Assert.That(stored.ExpiresAt).IsLessThan(DateTimeOffset.UtcNow.AddDays(3.1));
        await Assert.That(stored.Role).IsEqualTo(MemberRole.Member);
        // Nowhere in the database is the token itself
        await Assert.That(Factory.Db.Queryable<TenantInvite>().Where(i => i.TokenHash == token).Any()).IsFalse();

        var listed = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Invites");
        var only = listed.EnumerateArray().Single();
        await Assert.That(only.GetProperty("status").GetString()).IsEqualTo("Active");
        await Assert.That(only.GetProperty("uses").GetInt32()).IsEqualTo(0);
        await Assert.That(only.TryGetProperty("token", out _)).IsFalse();
    }

    [Test]
    public async Task What_will_not_do_for_a_link_is_refused()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var theirUnit = other.AddUnit("Theirs");

        var responses = new[]
        {
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { expiresInDays = 0 }),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { expiresInDays = 31 }),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { maxUses = 0 }),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { maxUses = 101 }),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { role = "Owner" }),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { unitId = theirUnit }),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.BadRequest]);
        await Assert.That(Factory.Db.Queryable<TenantInvite>().Count(i => i.TenantId == org.TenantId)).IsEqualTo(0);
    }

    [Test]
    public async Task An_organisation_can_only_have_so_many_links_going_and_ones_that_have_stopped_do_not_count()
    {
        var org = await Factory.CreateOrgAsync();
        var made = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            made.Add((await InviteAsync(org.Admin, org.Slug)).Id);
        }

        var over = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { });
        await Assert.That(over).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await over.Content.ReadAsStringAsync()).Contains("invite-limit");

        (await org.Admin.DeleteAsync($"/t/{org.Slug}/Invites/{made[0]}")).EnsureSuccessStatusCode();
        Factory.Db.Updateable<TenantInvite>().SetColumns(i => new TenantInvite { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) }).Where(i => i.Id == made[1]).ExecuteCommand();
        Factory.Db.Updateable<TenantInvite>().SetColumns(i => new TenantInvite { Uses = i.MaxUses }).Where(i => i.Id == made[2]).ExecuteCommand();

        for (var i = 0; i < 3; i++)
        {
            await InviteAsync(org.Admin, org.Slug);
        }

        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { })).HasStatus(HttpStatusCode.Forbidden);
        // Another organisation has its own
        var other = await Factory.CreateOrgAsync();
        await InviteAsync(other.Admin, other.Slug);
    }

    [Test]
    public async Task Only_admins_manage_links()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        var (id, _) = await InviteAsync(org.Admin, org.Slug);

        var responses = new[]
        {
            await member.GetAsync($"/t/{org.Slug}/Invites"),
            await member.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { }),
            await member.DeleteAsync($"/t/{org.Slug}/Invites/{id}"),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.Forbidden]);
        await Assert.That(Stored(id).RevokedAt).IsNull();
        await Assert.That(Factory.Db.Queryable<TenantInvite>().Count(i => i.TenantId == org.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task A_link_that_is_revoked_stops_working_and_another_organisations_cannot_be()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var (id, token) = await InviteAsync(org.Admin, org.Slug);
        var (theirs, _) = await InviteAsync(other.Admin, other.Slug);
        var person = NewPerson();

        Task<HttpResponseMessage> Preview() => person.GetAsync($"/Invites/{token}");
        await Assert.That(await Preview()).HasStatus(HttpStatusCode.OK);

        await Assert.That(await org.Admin.DeleteAsync($"/t/{org.Slug}/Invites/{id}")).HasStatus(HttpStatusCode.NoContent);
        var revokedAt = Stored(id).RevokedAt;
        await Assert.That(await org.Admin.DeleteAsync($"/t/{org.Slug}/Invites/{id}")).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(Stored(id).RevokedAt).IsEqualTo(revokedAt);

        await Assert.That(await Preview()).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await person.PostAsJsonAsync($"/Invites/{token}/Accept", new { })).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await org.Admin.DeleteAsync($"/t/{org.Slug}/Invites/{theirs}")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(Stored(theirs).RevokedAt).IsNull();
        await Assert.That(Factory.Db.Queryable<TenantMember>().Any(m => m.TenantId == org.TenantId && m.DisplayName == "New Person")).IsFalse();
    }

    [Test]
    public async Task Somebody_signed_in_can_see_where_a_link_leads_and_a_link_that_does_not_work_is_not_found_whatever_is_wrong_with_it()
    {
        var org = await Factory.CreateOrgAsync(orgName: "Alpha Company");
        var person = NewPerson();
        var (_, good) = await InviteAsync(org.Admin, org.Slug);
        var (expired, expiredToken) = await InviteAsync(org.Admin, org.Slug);
        var (usedUp, usedUpToken) = await InviteAsync(org.Admin, org.Slug, new { maxUses = 1 });
        var (_, suspendedToken) = await InviteAsync(org.Admin, org.Slug);
        Factory.Db.Updateable<TenantInvite>().SetColumns(i => new TenantInvite { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }).Where(i => i.Id == expired).ExecuteCommand();
        Factory.Db.Updateable<TenantInvite>().SetColumns(i => new TenantInvite { Uses = 1 }).Where(i => i.Id == usedUp).ExecuteCommand();

        var preview = await person.GetAsync($"/Invites/{good}");
        await Assert.That(preview).HasStatus(HttpStatusCode.OK);
        var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("organizationName").GetString()).IsEqualTo("Alpha Company");
        await Assert.That(body.GetProperty("requiresApproval").GetBoolean()).IsTrue();

        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { Status = TenantStatus.Suspended }).Where(t => t.Id == org.TenantId).ExecuteCommand();
        var bodies = new List<string>();
        foreach (var token in new[] { expiredToken, usedUpToken, suspendedToken, "not-a-token", new string('a', 43) })
        {
            var response = await person.GetAsync($"/Invites/{token}");
            await Assert.That(response).HasStatus(HttpStatusCode.NotFound);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        await Assert.That(bodies.Distinct().Count()).IsEqualTo(1);
        using var nobody = Factory.CreateClient();
        await Assert.That(await nobody.GetAsync($"/Invites/{good}")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await nobody.PostAsJsonAsync($"/Invites/{good}/Accept", new { })).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Joining_waits_for_an_admin_by_default_and_then_they_are_let_in()
    {
        var org = await Factory.CreateOrgAsync();
        var unit = org.AddUnit("Alpha");
        var (id, token) = await InviteAsync(org.Admin, org.Slug, new { unitId = unit });
        var person = NewPerson("Joiner");

        var accepted = await person.PostAsJsonAsync($"/Invites/{token}/Accept", new { });

        await Assert.That(accepted).HasStatus(HttpStatusCode.OK);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("slug").GetString()).IsEqualTo(org.Slug);
        await Assert.That(body.GetProperty("status").GetString()).IsEqualTo("Pending");
        await Assert.That(Stored(id).Uses).IsEqualTo(1);
        // Not in yet
        var waiting = await person.GetAsync($"/t/{org.Slug}");
        await Assert.That(waiting).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await waiting.Content.ReadAsStringAsync()).Contains("pending");
        var me = await person.GetFromJsonAsync<JsonElement>("/Me");
        await Assert.That(me.GetProperty("memberships").EnumerateArray().Single().GetProperty("status").GetString()).IsEqualTo("Pending");

        var members = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Members");
        var pending = members.EnumerateArray().Single(m => m.GetProperty("status").GetString() == "Pending");
        await Assert.That(pending.GetProperty("displayName").GetString()).IsEqualTo("Joiner");
        await Assert.That(pending.GetProperty("unitId").GetGuid()).IsEqualTo(unit);
        var approve = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{pending.GetProperty("id").GetGuid()}", new { displayName = "Joiner", unitId = unit, role = "Member", notificationScope = "None", membership = "In" });
        await Assert.That(approve).HasStatus(HttpStatusCode.OK);

        await Assert.That(await person.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Without_approval_they_are_in_at_once_with_what_the_link_says_and_the_name_they_give()
    {
        var org = await Factory.CreateOrgAsync();
        await SetRequireApprovalAsync(org, false);
        var unit = org.AddUnit("Alpha");
        var (_, token) = await InviteAsync(org.Admin, org.Slug, new { unitId = unit, role = "Admin" });
        var plain = NewPerson("Plain Name");
        var named = NewPerson("Plain Name");

        var first = await plain.PostAsJsonAsync($"/Invites/{token}/Accept", new { });
        var second = await named.PostAsJsonAsync($"/Invites/{token}/Accept", new { displayName = " Chosen Name " });

        await Assert.That((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()).IsEqualTo("Active");
        await Assert.That(await second.Content.ReadFromJsonAsync<JsonElement>()).IsNotNull();
        var members = Factory.Db.Queryable<TenantMember>().Where(m => m.TenantId == org.TenantId && m.UserId != null && m.DisplayName != "Founder").ToList();
        await Assert.That(members.Select(m => m.DisplayName)).IsEquivalentTo(["Plain Name", "Chosen Name"]);
        await Assert.That(members.All(m => m.Role == MemberRole.Admin && m.UnitId == unit && m.Status == MemberStatus.Active)).IsTrue();
        await Assert.That(await plain.GetAsync($"/t/{org.Slug}/Settings")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Joining_again_changes_nothing_and_uses_nothing_and_does_not_put_someone_who_is_in_back_to_waiting()
    {
        var org = await Factory.CreateOrgAsync();
        var (id, token) = await InviteAsync(org.Admin, org.Slug);
        var person = NewPerson();
        (await person.PostAsJsonAsync($"/Invites/{token}/Accept", new { })).EnsureSuccessStatusCode();
        var memberId = Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId && m.DisplayName == "New Person").Id;
        (await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{memberId}", new { displayName = "New Person", role = "Member", notificationScope = "None", membership = "In" })).EnsureSuccessStatusCode();

        var again = await person.PostAsJsonAsync($"/Invites/{token}/Accept", new { });

        await Assert.That(again).HasStatus(HttpStatusCode.OK);
        await Assert.That((await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()).IsEqualTo("Active");
        await Assert.That(Stored(id).Uses).IsEqualTo(1);
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId && m.DisplayName == "New Person")).IsEqualTo(1);
        // Even when the link has since been revoked, they are still told where they stand
        (await org.Admin.DeleteAsync($"/t/{org.Slug}/Invites/{id}")).EnsureSuccessStatusCode();
        await Assert.That(await person.PostAsJsonAsync($"/Invites/{token}/Accept", new { })).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Somebody_an_admin_removed_cannot_come_back_with_a_link()
    {
        var org = await Factory.CreateOrgAsync();
        var (id, token) = await InviteAsync(org.Admin, org.Slug);
        var (gone, _) = await org.AddMemberAsync("Gone", status: MemberStatus.Removed);

        var response = await gone.PostAsJsonAsync($"/Invites/{token}/Accept", new { });

        await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("removed");
        await Assert.That(Stored(id).Uses).IsEqualTo(0);
        await Assert.That(await gone.GetAsync($"/t/{org.Slug}")).HasStatus(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_link_lets_in_no_more_people_than_it_allows_even_when_they_join_at_the_same_moment()
    {
        var org = await Factory.CreateOrgAsync();
        var (id, token) = await InviteAsync(org.Admin, org.Slug, new { maxUses = 3 });
        var people = Enumerable.Range(0, 10).Select(i => NewPerson($"Person {i}")).ToList();

        List<Task<HttpResponseMessage>> requests;
        using (ExecutionContext.SuppressFlow())
        {
            requests = people.Select(p => Task.Run(() => p.PostAsJsonAsync($"/Invites/{token}/Accept", new { }))).ToList();
        }

        var statuses = (await Task.WhenAll(requests)).Select(r => r.StatusCode).ToList();

        await Assert.That(statuses.Count(s => s == HttpStatusCode.OK)).IsEqualTo(3);
        await Assert.That(statuses.Count(s => s == HttpStatusCode.NotFound)).IsEqualTo(7);
        await Assert.That(Stored(id).Uses).IsEqualTo(3);
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId && m.DisplayName.StartsWith("Person"))).IsEqualTo(3);
    }

    [Test]
    public async Task The_same_person_joining_at_the_same_moment_is_one_member_and_one_use()
    {
        var org = await Factory.CreateOrgAsync();
        var (id, token) = await InviteAsync(org.Admin, org.Slug);
        var person = NewPerson("Eager");

        List<Task<HttpResponseMessage>> requests;
        using (ExecutionContext.SuppressFlow())
        {
            requests = Enumerable.Range(0, 6).Select(_ => Task.Run(() => person.PostAsJsonAsync($"/Invites/{token}/Accept", new { }))).ToList();
        }

        var statuses = (await Task.WhenAll(requests)).Select(r => r.StatusCode).Distinct().ToList();

        await Assert.That(statuses).IsEquivalentTo([HttpStatusCode.OK]);
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId && m.DisplayName == "Eager")).IsEqualTo(1);
        await Assert.That(Stored(id).Uses).IsEqualTo(1);
    }

    [Test]
    public async Task A_link_only_leads_to_its_own_organisation()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var (_, token) = await InviteAsync(org.Admin, org.Slug);
        var person = NewPerson("Traveller");

        (await person.PostAsJsonAsync($"/Invites/{token}/Accept", new { })).EnsureSuccessStatusCode();

        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId && m.DisplayName == "Traveller")).IsEqualTo(1);
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == other.TenantId && m.DisplayName == "Traveller")).IsEqualTo(0);
        await Assert.That(await person.GetAsync($"/t/{other.Slug}")).HasStatus(HttpStatusCode.NotFound);
    }
}
