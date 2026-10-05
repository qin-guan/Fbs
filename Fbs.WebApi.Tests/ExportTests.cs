using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;

namespace Fbs.WebApi.Tests;

/// <summary>A copy of what is kept, for whoever it is about, and for the admins of an organisation.</summary>
public class ExportTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static object Booking(Guid facilityId, int day, string conduct = "Lesson") => new
    {
        conduct,
        description = "Room 2",
        pocName = "LTA Contact",
        pocPhone = "+6591234567",
        slots = new[] { new { facilityId, startDateTime = DateTimeOffset.UtcNow.Date.AddDays(day).AddHours(2), endDateTime = DateTimeOffset.UtcNow.Date.AddDays(day).AddHours(3) } },
    };

    private static async Task<Guid> BookAsync(HttpClient client, string slug, Guid facilityId, int day, string conduct = "Lesson")
    {
        var response = await client.PostAsJsonAsync($"/t/{slug}/Bookings", Booking(facilityId, day, conduct));
        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single().GetProperty("id").GetGuid();
    }

    private static Guid[] Ids(JsonElement array) => array.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToArray();

    [Test]
    public async Task A_person_can_download_what_is_kept_about_them_across_organisations_and_nothing_about_anybody_else()
    {
        var first = await Factory.CreateOrgAsync(orgName: "First Org");
        var second = await Factory.CreateOrgAsync(orgName: "Second Org");
        var unit = first.AddUnit("Alpha");
        var hall = first.AddFacility("Hall");
        var field = second.AddFacility("Field");
        var (person, memberId) = await first.AddMemberAsync("Sam Tan", unitId: unit, phone: "+6590000001");
        var accountId = await Factory.AccountIdOfAsync(person);
        var (stranger, _) = await first.AddMemberAsync("Somebody Else", phone: "+6590000002");
        var strangerBooking = await BookAsync(stranger, first.Slug, hall, 8, "Not Sam's");

        // They are in a second organisation too, waiting to be let in, and have linked Telegram
        Factory.Db.Insertable(new TenantMember { Id = Guid.NewGuid(), TenantId = second.TenantId, UserId = accountId, DisplayName = "Sam T", Status = MemberStatus.Pending }).ExecuteCommand();
        Factory.Db.Insertable(new TelegramLink { Id = Guid.NewGuid(), UserId = accountId, ChatId = "424242", LinkedAt = DateTimeOffset.UtcNow }).ExecuteCommand();
        var kept = await BookAsync(person, first.Slug, hall, 3, "Circuit");
        var cancelled = await BookAsync(person, first.Slug, hall, 4, "Cancelled one");
        (await person.DeleteAsync($"/t/{first.Slug}/Bookings/{cancelled}")).EnsureSuccessStatusCode();
        // Somebody else's booking that they changed: an admin, in their organisation, cancelling it
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { Role = MemberRole.Admin }).Where(m => m.Id == memberId).ExecuteCommand();
        (await person.DeleteAsync($"/t/{first.Slug}/Bookings/{strangerBooking}")).EnsureSuccessStatusCode();
        _ = field;

        var response = await person.GetAsync("/Me/Export");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentDisposition?.DispositionType).IsEqualTo("attachment");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("account").GetProperty("id").GetGuid()).IsEqualTo(accountId);
        await Assert.That(body.GetProperty("account").GetProperty("name").GetString()).IsEqualTo("Sam Tan");
        await Assert.That(body.GetProperty("telegram").GetProperty("linked").GetBoolean()).IsTrue();
        await Assert.That(body.GetProperty("telegram").GetProperty("chatId").GetString()).IsEqualTo("424242");

        var memberships = body.GetProperty("memberships").EnumerateArray().ToList();
        await Assert.That(memberships.Select(m => m.GetProperty("organizationName").GetString())).IsEquivalentTo(["First Org", "Second Org"]);
        var inFirst = memberships.Single(m => m.GetProperty("organizationSlug").GetString() == first.Slug);
        await Assert.That(inFirst.GetProperty("unit").GetString()).IsEqualTo("Alpha");
        await Assert.That(inFirst.GetProperty("phone").GetString()).IsEqualTo("+6590000001");
        await Assert.That(memberships.Single(m => m.GetProperty("organizationSlug").GetString() == second.Slug).GetProperty("status").GetString()).IsEqualTo("Pending");

        // What they booked, cancelled or not, with who to contact as it was written; and not what somebody else booked
        var bookings = body.GetProperty("bookings");
        await Assert.That(Ids(bookings)).IsEquivalentTo([kept, cancelled]);
        var keptEntry = bookings.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == kept);
        await Assert.That(keptEntry.GetProperty("facility").GetString()).IsEqualTo("Hall");
        await Assert.That(keptEntry.GetProperty("pocName").GetString()).IsEqualTo("LTA Contact");
        await Assert.That(bookings.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == cancelled).GetProperty("cancelledAt").ValueKind).IsEqualTo(JsonValueKind.String);

        // What they changed of somebody else's is only that it was, and when: not what it was for
        var others = body.GetProperty("othersBookingsTheyChanged");
        await Assert.That(Ids(others)).IsEquivalentTo([strangerBooking]);
        await Assert.That(others[0].GetProperty("what").GetString()).IsEqualTo("cancelled");
        var text = body.GetRawText();
        await Assert.That(text).DoesNotContain("Not Sam's");
        await Assert.That(text).DoesNotContain("Somebody Else");
        await Assert.That(text).DoesNotContain("+6590000002");
    }

    [Test]
    public async Task Somebody_with_no_organisation_gets_their_account_and_nothing_else_and_it_needs_signing_in()
    {
        var person = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Lonely Person");

        var body = await person.GetFromJsonAsync<JsonElement>("/Me/Export");

        await Assert.That(body.GetProperty("account").GetProperty("name").GetString()).IsEqualTo("Lonely Person");
        await Assert.That(body.GetProperty("telegram").GetProperty("linked").GetBoolean()).IsFalse();
        await Assert.That(body.GetProperty("memberships").GetArrayLength()).IsEqualTo(0);
        await Assert.That(body.GetProperty("bookings").GetArrayLength()).IsEqualTo(0);

        using var nobody = Factory.CreateClient();
        await Assert.That(await nobody.GetAsync("/Me/Export")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task It_is_limited_for_each_person_and_one_person_using_theirs_up_leaves_another_theirs()
    {
        var busy = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        var other = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        for (var i = 0; i < 5; i++)
        {
            await Assert.That(await busy.GetAsync("/Me/Export")).HasStatus(HttpStatusCode.OK);
        }

        var over = await busy.GetAsync("/Me/Export");
        await Assert.That(over).HasStatus(HttpStatusCode.TooManyRequests);
        await Assert.That(over.Headers.Contains("Retry-After")).IsTrue();
        await Assert.That(await other.GetAsync("/Me/Export")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_admin_can_download_everything_of_their_organisation_and_only_theirs()
    {
        var org = await Factory.CreateOrgAsync(orgName: "Exported Org");
        var other = await Factory.CreateOrgAsync(orgName: "Other Org");
        var unit = org.AddUnit("Alpha");
        var hall = org.AddFacility("Hall", availableToAll: false, unit);
        var (member, memberId) = await org.AddMemberAsync("A Member", unitId: unit, phone: "+6590000003");
        await org.AddMemberAsync("Removed One", status: MemberStatus.Removed, phone: "+6590000004");
        var kept = await BookAsync(member, org.Slug, hall, 3, "Kept");
        var cancelled = await BookAsync(org.Admin, org.Slug, hall, 4, "Cancelled");
        (await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{cancelled}")).EnsureSuccessStatusCode();
        var invite = await (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var token = invite.GetProperty("token").GetString()!;
        var elsewhere = other.AddFacility("Elsewhere");
        await BookAsync(other.Admin, other.Slug, elsewhere, 5, "Somebody else's");
        await other.AddMemberAsync("Other Member", phone: "+6590000005");

        var response = await org.Admin.GetAsync($"/t/{org.Slug}/Export");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentDisposition?.FileName?.Trim('"')).IsEqualTo($"{org.Slug}-data.json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("organization").GetProperty("name").GetString()).IsEqualTo("Exported Org");
        await Assert.That(body.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("name").GetString())).IsEquivalentTo(["Alpha"]);
        var facility = body.GetProperty("facilities").EnumerateArray().Single();
        await Assert.That(facility.GetProperty("name").GetString()).IsEqualTo("Hall");
        await Assert.That(facility.GetProperty("unitIds").EnumerateArray().Select(i => i.GetGuid())).IsEquivalentTo([unit]);

        // Everybody in it, removed ones and phone numbers included
        var members = body.GetProperty("members").EnumerateArray().ToList();
        await Assert.That(members.Select(m => m.GetProperty("displayName").GetString())).IsEquivalentTo(["A Member", "Removed One", "Founder"]);
        await Assert.That(members.Single(m => m.GetProperty("id").GetGuid() == memberId).GetProperty("phone").GetString()).IsEqualTo("+6590000003");
        await Assert.That(members.Single(m => m.GetProperty("displayName").GetString() == "Removed One").GetProperty("status").GetString()).IsEqualTo("Removed");

        // Every booking, cancelled ones and who to contact included
        var bookings = body.GetProperty("bookings").EnumerateArray().ToList();
        await Assert.That(bookings.Select(b => b.GetProperty("id").GetGuid())).IsEquivalentTo([kept, cancelled]);
        await Assert.That(bookings.Single(b => b.GetProperty("id").GetGuid() == kept).GetProperty("pocPhone").GetString()).IsEqualTo("+6591234567");
        await Assert.That(bookings.Single(b => b.GetProperty("id").GetGuid() == cancelled).GetProperty("cancelledAt").ValueKind).IsEqualTo(JsonValueKind.String);

        // The links that were made, but not what could be used to join with
        await Assert.That(body.GetProperty("invites").GetArrayLength()).IsEqualTo(1);
        var text = body.GetRawText();
        await Assert.That(text).DoesNotContain(token);
        await Assert.That(text).DoesNotContain(Fbs.WebApi.Tenancy.InviteTokens.Hash(token));
        await Assert.That(body.GetProperty("history").GetArrayLength()).IsGreaterThan(0);

        // And nothing of another organisation
        await Assert.That(text).DoesNotContain("Somebody else's");
        await Assert.That(text).DoesNotContain("Other Member");
        await Assert.That(text).DoesNotContain("+6590000005");
    }

    [Test]
    public async Task Only_an_admin_of_it_can_and_taking_a_copy_is_written_in_its_history()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        using var nobody = Factory.CreateClient();

        await Assert.That(await member.GetAsync($"/t/{org.Slug}/Export")).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await other.Admin.GetAsync($"/t/{org.Slug}/Export")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That((await nobody.GetAsync($"/t/{org.Slug}/Export")).StatusCode).IsNotEqualTo(HttpStatusCode.OK);
        await Assert.That(Factory.Db.Queryable<AuditEntry>().Count(e => e.TenantId == org.TenantId && e.Action == "tenant.exported")).IsEqualTo(0);

        (await org.Admin.GetAsync($"/t/{org.Slug}/Export")).EnsureSuccessStatusCode();

        var written = Factory.Db.Queryable<AuditEntry>().Where(e => e.TenantId == org.TenantId && e.Action == "tenant.exported").ToList().Single();
        await Assert.That(written.ActorMemberId).IsNotNull();
        var history = (await (await org.Admin.GetAsync($"/t/{org.Slug}/Export")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("history");
        await Assert.That(history.EnumerateArray().Any(e => e.GetProperty("action").GetString() == "tenant.exported")).IsTrue();
    }

    [Test]
    public async Task A_copy_of_an_organisation_is_limited_for_the_person_asking_and_needs_it_to_be_available()
    {
        var org = await Factory.CreateOrgAsync();
        for (var i = 0; i < 5; i++)
        {
            await Assert.That(await org.Admin.GetAsync($"/t/{org.Slug}/Export")).HasStatus(HttpStatusCode.OK);
        }

        await Assert.That(await org.Admin.GetAsync($"/t/{org.Slug}/Export")).HasStatus(HttpStatusCode.TooManyRequests);

        var suspended = await Factory.CreateOrgAsync();
        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { Status = TenantStatus.Suspended }).Where(t => t.Id == suspended.TenantId).ExecuteCommand();
        await Assert.That(await suspended.Admin.GetAsync($"/t/{suspended.Slug}/Export")).HasStatus(HttpStatusCode.Forbidden);
    }
}
