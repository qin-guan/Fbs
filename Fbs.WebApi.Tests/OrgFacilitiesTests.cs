using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Tests;

/// <summary>The facilities of an organisation, and which units can book them.</summary>
public class OrgFacilitiesTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static async Task<Guid> MakeAsync(HttpClient admin, string slug, object body)
    {
        var response = await admin.PostAsJsonAsync($"/t/{slug}/Facilities", body);
        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private List<Guid> AccessOf(Guid facilityId) => Factory.Db.Queryable<FacilityUnitAccess>().Where(a => a.FacilityId == facilityId).Select(a => a.UnitId).ToList();

    [Test]
    public async Task An_admin_makes_a_facility_that_is_open_to_everyone_or_to_some_units()
    {
        var org = await Factory.CreateOrgAsync();
        var alpha = org.AddUnit("Alpha");
        var bravo = org.AddUnit("Bravo");

        var open = await MakeAsync(org.Admin, org.Slug, new { name = "Parade Square", @group = "Outdoor", availableToAll = true, unitIds = Array.Empty<Guid>() });
        var limited = await MakeAsync(org.Admin, org.Slug, new { name = " Gym ", availableToAll = false, unitIds = new[] { alpha, bravo, alpha } });

        var listed = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Facilities");
        var byName = listed.EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!);
        await Assert.That(byName.Keys).IsEquivalentTo(["Gym", "Parade Square"]);
        await Assert.That(byName["Parade Square"].GetProperty("availableToAll").GetBoolean()).IsTrue();
        await Assert.That(byName["Parade Square"].GetProperty("group").GetString()).IsEqualTo("Outdoor");
        await Assert.That(byName["Gym"].GetProperty("unitIds").EnumerateArray().Select(u => u.GetGuid())).IsEquivalentTo([alpha, bravo]);
        await Assert.That(AccessOf(limited)).IsEquivalentTo([alpha, bravo]);
        await Assert.That(AccessOf(open)).IsEmpty();
    }

    [Test]
    public async Task Changing_a_facility_replaces_who_can_book_it()
    {
        var org = await Factory.CreateOrgAsync();
        var alpha = org.AddUnit("Alpha");
        var bravo = org.AddUnit("Bravo");
        var id = await MakeAsync(org.Admin, org.Slug, new { name = "Gym", availableToAll = false, unitIds = new[] { alpha } });

        var toBravo = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Facilities/{id}", new { name = "Gym 2", @group = "Indoor", availableToAll = false, unitIds = new[] { bravo } });
        await Assert.That(toBravo).HasStatus(HttpStatusCode.OK);
        await Assert.That(AccessOf(id)).IsEquivalentTo([bravo]);

        var toAll = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Facilities/{id}", new { name = "Gym 2", availableToAll = true, unitIds = Array.Empty<Guid>() });
        await Assert.That(toAll).HasStatus(HttpStatusCode.OK);
        var stored = Factory.Db.Queryable<DataFacility>().First(f => f.Id == id);
        await Assert.That(stored.AvailableToAll).IsTrue();
        await Assert.That(stored.Group).IsNull();
        await Assert.That(AccessOf(id)).IsEmpty();
    }

    [Test]
    public async Task What_will_not_do_is_refused_and_changes_nothing()
    {
        var org = await Factory.CreateOrgAsync();
        var alpha = org.AddUnit("Alpha");
        var id = await MakeAsync(org.Admin, org.Slug, new { name = "Gym", availableToAll = false, unitIds = new[] { alpha } });

        var noName = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Facilities", new { name = " ", availableToAll = true, unitIds = Array.Empty<Guid>() });
        var both = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Facilities/{id}", new { name = "Gym", availableToAll = true, unitIds = new[] { alpha } });
        var duplicate = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Facilities", new { name = "Gym", availableToAll = true, unitIds = Array.Empty<Guid>() });

        await Assert.That(noName).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(both).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(duplicate).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await duplicate.Content.ReadAsStringAsync()).Contains("facility-exists");
        await Assert.That(Factory.Db.Queryable<DataFacility>().Count(f => f.TenantId == org.TenantId)).IsEqualTo(1);
        await Assert.That(Factory.Db.Queryable<DataFacility>().First(f => f.Id == id).AvailableToAll).IsFalse();
        await Assert.That(AccessOf(id)).IsEquivalentTo([alpha]);
    }

    [Test]
    public async Task A_unit_of_another_organisation_cannot_be_given_access()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var theirs = other.AddUnit("Theirs");
        var mine = org.AddUnit("Mine");
        var id = await MakeAsync(org.Admin, org.Slug, new { name = "Gym", availableToAll = false, unitIds = new[] { mine } });

        var create = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Facilities", new { name = "Hall", availableToAll = false, unitIds = new[] { theirs } });
        var change = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Facilities/{id}", new { name = "Gym", availableToAll = false, unitIds = new[] { mine, theirs } });

        await Assert.That(create).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await create.Content.ReadAsStringAsync()).Contains("unit-unknown");
        await Assert.That(change).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(Factory.Db.Queryable<DataFacility>().Count(f => f.TenantId == org.TenantId)).IsEqualTo(1);
        await Assert.That(AccessOf(id)).IsEquivalentTo([mine]);
        await Assert.That(Factory.Db.Queryable<FacilityUnitAccess>().Any(a => a.UnitId == theirs)).IsFalse();
    }

    [Test]
    public async Task A_facility_nothing_was_booked_on_is_deleted_with_who_could_book_it_and_one_that_was_is_kept()
    {
        var org = await Factory.CreateOrgAsync();
        var alpha = org.AddUnit("Alpha");
        var unused = await MakeAsync(org.Admin, org.Slug, new { name = "Unused", availableToAll = false, unitIds = new[] { alpha } });
        var used = await MakeAsync(org.Admin, org.Slug, new { name = "Used", availableToAll = true, unitIds = Array.Empty<Guid>() });
        var (_, memberId) = await org.AddMemberAsync();
        // A booking that was cancelled is still one, as it is kept
        Factory.Db.Insertable(
                new DataBooking
                {
                    Id = Guid.NewGuid(),
                    TenantId = org.TenantId,
                    FacilityId = used,
                    StartUtc = DateTimeOffset.UtcNow.AddDays(-1),
                    EndUtc = DateTimeOffset.UtcNow.AddDays(-1).AddHours(1),
                    Conduct = "Lesson",
                    BookedByMemberId = memberId,
                    CancelledAt = DateTimeOffset.UtcNow,
                }
            )
            .ExecuteCommand();

        var keeps = await org.Admin.DeleteAsync($"/t/{org.Slug}/Facilities/{used}");
        var goes = await org.Admin.DeleteAsync($"/t/{org.Slug}/Facilities/{unused}");

        await Assert.That(keeps).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await keeps.Content.ReadAsStringAsync()).Contains("facility-in-use");
        await Assert.That(goes).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(Factory.Db.Queryable<DataFacility>().Where(f => f.TenantId == org.TenantId).Select(f => f.Name).ToList()).IsEquivalentTo(["Used"]);
        await Assert.That(Factory.Db.Queryable<FacilityUnitAccess>().Any(a => a.FacilityId == unused)).IsFalse();
    }

    [Test]
    public async Task Only_admins_manage_facilities()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();
        var id = await MakeAsync(org.Admin, org.Slug, new { name = "Gym", availableToAll = true, unitIds = Array.Empty<Guid>() });
        var body = new { name = "Mine", availableToAll = true, unitIds = Array.Empty<Guid>() };

        var responses = new[]
        {
            await member.GetAsync($"/t/{org.Slug}/Facilities"),
            await member.PostAsJsonAsync($"/t/{org.Slug}/Facilities", body),
            await member.PutAsJsonAsync($"/t/{org.Slug}/Facilities/{id}", body),
            await member.DeleteAsync($"/t/{org.Slug}/Facilities/{id}"),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.Forbidden]);
        await Assert.That(Factory.Db.Queryable<DataFacility>().Where(f => f.TenantId == org.TenantId).Select(f => f.Name).ToList()).IsEquivalentTo(["Gym"]);
    }

    [Test]
    public async Task A_facility_of_another_organisation_is_not_seen_or_changed()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        await MakeAsync(org.Admin, org.Slug, new { name = "Mine", availableToAll = true, unitIds = Array.Empty<Guid>() });
        var theirs = await MakeAsync(other.Admin, other.Slug, new { name = "Theirs", availableToAll = true, unitIds = Array.Empty<Guid>() });

        var listed = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Facilities");
        var change = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Facilities/{theirs}", new { name = "Taken", availableToAll = true, unitIds = Array.Empty<Guid>() });
        var delete = await org.Admin.DeleteAsync($"/t/{org.Slug}/Facilities/{theirs}");

        await Assert.That(listed.EnumerateArray().Select(f => f.GetProperty("name").GetString())).IsEquivalentTo(["Mine"]);
        await Assert.That(change).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(delete).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(Factory.Db.Queryable<DataFacility>().First(f => f.Id == theirs).Name).IsEqualTo("Theirs");
    }
}
