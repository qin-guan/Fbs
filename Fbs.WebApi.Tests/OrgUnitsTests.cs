using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tests;

/// <summary>The units of an organisation: who can list them, and that only its admins change them.</summary>
public class OrgUnitsTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static async Task<Guid> MakeAsync(HttpClient admin, string slug, string name)
    {
        var response = await admin.PostAsJsonAsync($"/t/{slug}/Units", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Test]
    public async Task An_admin_makes_renames_and_deletes_units_and_every_member_can_list_them()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();

        var alpha = await MakeAsync(org.Admin, org.Slug, " Alpha ");
        await MakeAsync(org.Admin, org.Slug, "Bravo");
        var renamed = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Units/{alpha}", new { name = "Alpha Company" });

        await Assert.That(renamed).HasStatus(HttpStatusCode.OK);
        var listed = await member.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Units");
        await Assert.That(listed.EnumerateArray().Select(u => u.GetProperty("name").GetString())).IsEquivalentTo(["Alpha Company", "Bravo"]);

        await Assert.That(await org.Admin.DeleteAsync($"/t/{org.Slug}/Units/{alpha}")).HasStatus(HttpStatusCode.NoContent);
        var after = await member.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Units");
        await Assert.That(after.EnumerateArray().Select(u => u.GetProperty("name").GetString())).IsEquivalentTo(["Bravo"]);
    }

    [Test]
    public async Task A_name_that_is_empty_or_too_long_is_refused()
    {
        var org = await Factory.CreateOrgAsync();

        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "  " })).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = new string('x', 101) })).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(Factory.Db.Queryable<DataUnit>().Count(u => u.TenantId == org.TenantId)).IsEqualTo(0);
    }

    [Test]
    public async Task Two_units_of_an_organisation_cannot_share_a_name_but_two_organisations_can()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        await MakeAsync(org.Admin, org.Slug, "Alpha");
        var bravo = await MakeAsync(org.Admin, org.Slug, "Bravo");

        var again = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Alpha" });
        var rename = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Units/{bravo}", new { name = "Alpha" });

        await Assert.That(again).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await again.Content.ReadAsStringAsync()).Contains("unit-exists");
        await Assert.That(rename).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(Factory.Db.Queryable<DataUnit>().First(u => u.Id == bravo).Name).IsEqualTo("Bravo");
        await Assert.That(await other.Admin.PostAsJsonAsync($"/t/{other.Slug}/Units", new { name = "Alpha" })).HasStatus(HttpStatusCode.Created);
    }

    [Test]
    public async Task A_member_who_is_not_an_admin_cannot_change_units()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        var alpha = await MakeAsync(org.Admin, org.Slug, "Alpha");

        var create = await member.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Mine" });
        var rename = await member.PutAsJsonAsync($"/t/{org.Slug}/Units/{alpha}", new { name = "Mine" });
        var delete = await member.DeleteAsync($"/t/{org.Slug}/Units/{alpha}");

        await Assert.That(new[] { create, rename, delete }.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.Forbidden]);
        await Assert.That(Factory.Db.Queryable<DataUnit>().Where(u => u.TenantId == org.TenantId).Select(u => u.Name).ToList()).IsEquivalentTo(["Alpha"]);
    }

    [Test]
    public async Task A_unit_with_people_or_bookings_cannot_be_deleted_and_one_without_takes_what_referred_to_it_with_it()
    {
        var org = await Factory.CreateOrgAsync();
        var withPeople = await MakeAsync(org.Admin, org.Slug, "With people");
        var withBookings = await MakeAsync(org.Admin, org.Slug, "With bookings");
        var empty = await MakeAsync(org.Admin, org.Slug, "Empty");
        var (_, memberId) = await org.AddMemberAsync(unitId: withPeople);
        var (_, gone) = await org.AddMemberAsync("Left", status: MemberStatus.Removed, unitId: empty);
        var facilityId = Guid.NewGuid();
        Factory.Db.Insertable(new DataFacility { Id = facilityId, TenantId = org.TenantId, Name = "Hall" }).ExecuteCommand();
        Factory.Db.Insertable(new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = org.TenantId, FacilityId = facilityId, UnitId = empty }).ExecuteCommand();
        Factory.Db.Insertable(
                new DataBooking
                {
                    Id = Guid.NewGuid(),
                    TenantId = org.TenantId,
                    FacilityId = facilityId,
                    StartUtc = DateTimeOffset.UtcNow.AddDays(1),
                    EndUtc = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
                    Conduct = "Lesson",
                    BookedByMemberId = memberId,
                    UnitId = withBookings,
                }
            )
            .ExecuteCommand();

        var people = await org.Admin.DeleteAsync($"/t/{org.Slug}/Units/{withPeople}");
        var bookings = await org.Admin.DeleteAsync($"/t/{org.Slug}/Units/{withBookings}");
        var none = await org.Admin.DeleteAsync($"/t/{org.Slug}/Units/{empty}");

        await Assert.That(people).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await people.Content.ReadAsStringAsync()).Contains("unit-in-use");
        await Assert.That(bookings).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(none).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(Factory.Db.Queryable<DataUnit>().Any(u => u.Id == empty)).IsFalse();
        await Assert.That(Factory.Db.Queryable<FacilityUnitAccess>().Any(a => a.UnitId == empty)).IsFalse();
        await Assert.That(Factory.Db.Queryable<TenantMember>().First(m => m.Id == gone).UnitId).IsNull();
        await Assert.That(Factory.Db.Queryable<DataUnit>().Count(u => u.TenantId == org.TenantId)).IsEqualTo(2);
    }

    [Test]
    public async Task What_belongs_to_another_organisation_is_not_seen_or_changed()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        await MakeAsync(org.Admin, org.Slug, "Mine");
        var theirs = await MakeAsync(other.Admin, other.Slug, "Theirs");

        var listed = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Units");
        // With the address of their own organisation and the ID of somebody else's unit
        var rename = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Units/{theirs}", new { name = "Taken" });
        var delete = await org.Admin.DeleteAsync($"/t/{org.Slug}/Units/{theirs}");
        // And the address of theirs, which they aren't in
        var direct = await org.Admin.DeleteAsync($"/t/{other.Slug}/Units/{theirs}");

        await Assert.That(listed.EnumerateArray().Select(u => u.GetProperty("name").GetString())).IsEquivalentTo(["Mine"]);
        await Assert.That(new[] { rename, delete, direct }.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.NotFound]);
        await Assert.That(Factory.Db.Queryable<DataUnit>().First(u => u.Id == theirs).Name).IsEqualTo("Theirs");
    }
}
