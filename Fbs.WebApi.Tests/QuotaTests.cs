using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tests;

/// <summary>What one organisation can have, and make in a day, so that it can't use up what they all share.</summary>
public class QuotaTests
{
    [ClassDataSource<SmallFactory>]
    public required SmallFactory Factory { get; init; }

    /// <summary>Two units and two facilities, four people, and three bookings in a day.</summary>
    public class SmallFactory : ClerkFbsApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Limits:MaxUnits", "2");
            builder.UseSetting("Limits:MaxFacilities", "2");
            builder.UseSetting("Limits:MaxMembers", "4");
            builder.UseSetting("Limits:MaxBookingsPerDay", "3");
        }
    }

    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private static DateTimeOffset At(int daysAhead, int hour)
    {
        var day = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore")).Date.AddDays(daysAhead);
        return new DateTimeOffset(day.AddHours(hour), Singapore);
    }

    private static object Book(Guid facilityId, params (int Days, int Hour)[] slots) =>
        new { conduct = "Lesson", slots = slots.Select(s => new { facilityId, startDateTime = At(s.Days, s.Hour), endDateTime = At(s.Days, s.Hour + 1) }).ToArray() };

    private static async Task<string> BodyOf(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    [Test]
    public async Task An_organisation_can_only_have_so_many_units_and_can_have_another_once_one_is_deleted()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var made = new List<Guid>();
        foreach (var name in new[] { "Alpha", "Bravo" })
        {
            var response = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name });
            await Assert.That(response).HasStatus(HttpStatusCode.Created);
            made.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        var over = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Charlie" });
        await Assert.That(over).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await BodyOf(over)).Contains("unit-limit");
        await Assert.That(Factory.Db.Queryable<DataUnit>().Count(u => u.TenantId == org.TenantId)).IsEqualTo(2);

        // Another organisation has its own
        await Assert.That(await other.Admin.PostAsJsonAsync($"/t/{other.Slug}/Units", new { name = "Alpha" })).HasStatus(HttpStatusCode.Created);

        (await org.Admin.DeleteAsync($"/t/{org.Slug}/Units/{made[0]}")).EnsureSuccessStatusCode();
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Charlie" })).HasStatus(HttpStatusCode.Created);
    }

    [Test]
    public async Task An_organisation_can_only_have_so_many_facilities_and_can_have_another_once_one_is_deleted()
    {
        var org = await Factory.CreateOrgAsync();
        var made = new List<Guid>();
        foreach (var name in new[] { "Hall", "Gym" })
        {
            var response = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Facilities", new { name, availableToAll = true });
            await Assert.That(response).HasStatus(HttpStatusCode.Created);
            made.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        var over = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Facilities", new { name = "Field", availableToAll = true });
        await Assert.That(over).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await BodyOf(over)).Contains("facility-limit");
        await Assert.That(Factory.Db.Queryable<DataFacility>().Count(f => f.TenantId == org.TenantId)).IsEqualTo(2);

        (await org.Admin.DeleteAsync($"/t/{org.Slug}/Facilities/{made[0]}")).EnsureSuccessStatusCode();
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Facilities", new { name = "Field", availableToAll = true })).HasStatus(HttpStatusCode.Created);
    }

    [Test]
    public async Task An_organisation_can_only_have_so_many_people_counting_those_waiting_and_those_added_but_not_those_removed()
    {
        var org = await Factory.CreateOrgAsync();
        // The founder is one, somebody waiting to be let in is another, and two are added by their phone numbers
        await org.AddMemberAsync("Waiting", status: MemberStatus.Pending);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", new { displayName = "One", phone = "+6590000001" })).HasStatus(HttpStatusCode.Created);
        var last = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", new { displayName = "Two", phone = "+6590000002" });
        await Assert.That(last).HasStatus(HttpStatusCode.Created);

        var over = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", new { displayName = "Three", phone = "+6590000003" });
        await Assert.That(over).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await BodyOf(over)).Contains("member-limit");
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId)).IsEqualTo(4);

        // Somebody removed no longer counts
        var lastId = (await last.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{lastId}", new { displayName = "Two", phone = "+6590000002", membership = "Removed" })).EnsureSuccessStatusCode();
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", new { displayName = "Three", phone = "+6590000003" })).HasStatus(HttpStatusCode.Created);

        // ... and letting them back in is one more
        var back = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{lastId}", new { displayName = "Two", phone = "+6590000002", membership = "In" });
        await Assert.That(back).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await BodyOf(back)).Contains("member-limit");
        await Assert.That(Factory.Db.Queryable<TenantMember>().First(m => m.Id == lastId).Status).IsEqualTo(MemberStatus.Removed);
    }

    [Test]
    public async Task Somebody_joining_with_a_link_is_refused_when_it_is_full_and_the_link_is_not_used_up_by_it()
    {
        var org = await Factory.CreateOrgAsync();
        for (var i = 0; i < 3; i++)
        {
            await org.AddMemberAsync($"Member {i}");
        }

        var invite = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { });
        var body = await invite.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var person = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Late Comer");

        var joined = await person.PostAsJsonAsync($"/Invites/{token}/Accept", new { });

        await Assert.That(joined).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await BodyOf(joined)).Contains("member-limit");
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == org.TenantId)).IsEqualTo(4);
        await Assert.That(Factory.Db.Queryable<TenantInvite>().First(i => i.Id == body.GetProperty("id").GetGuid()).Uses).IsEqualTo(0);
    }

    [Test]
    public async Task An_organisation_can_only_make_so_many_bookings_in_a_day_however_they_are_asked_for()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");

        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, (2, 9), (2, 11)))).HasStatus(HttpStatusCode.Created);

        // Two more would be four: none of them are made, not one
        var batch = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, (3, 9), (3, 11)));
        await Assert.That(batch).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await BodyOf(batch)).Contains("booking-limit");
        await Assert.That(Factory.Db.Queryable<DataBooking>().Count(b => b.TenantId == org.TenantId)).IsEqualTo(2);

        // One more is three, which is allowed, and the next is not
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, (4, 9)))).HasStatus(HttpStatusCode.Created);
        var over = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, (5, 9)));
        await Assert.That(over).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(Factory.Db.Queryable<DataBooking>().Count(b => b.TenantId == org.TenantId)).IsEqualTo(3);
    }

    [Test]
    public async Task Bookings_that_were_cancelled_still_count_and_those_from_before_the_last_day_do_not()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var ids = new List<Guid>();
        foreach (var day in new[] { 2, 3, 4 })
        {
            var made = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, (day, 9)));
            await Assert.That(made).HasStatus(HttpStatusCode.Created);
            ids.Add((await made.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single().GetProperty("id").GetGuid());
        }

        (await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{ids[0]}")).EnsureSuccessStatusCode();
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, (5, 9)))).HasStatus(HttpStatusCode.Forbidden);

        // Made two days ago, so it doesn't count any more
        Factory.Db.Updateable<DataBooking>().SetColumns(b => new DataBooking { CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) }).Where(b => b.Id == ids[0]).ExecuteCommand();
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, (5, 9)))).HasStatus(HttpStatusCode.Created);
    }

    [Test]
    public async Task What_one_organisation_has_or_has_made_does_not_count_against_another()
    {
        var busy = await Factory.CreateOrgAsync();
        var quiet = await Factory.CreateOrgAsync();
        var hall = busy.AddFacility("Hall");
        var quietHall = quiet.AddFacility("Hall");
        await Assert.That(await busy.Admin.PostAsJsonAsync($"/t/{busy.Slug}/Bookings", Book(hall, (2, 9), (2, 11), (2, 13)))).HasStatus(HttpStatusCode.Created);

        await Assert.That(await quiet.Admin.PostAsJsonAsync($"/t/{quiet.Slug}/Bookings", Book(quietHall, (2, 9), (2, 11), (2, 13)))).HasStatus(HttpStatusCode.Created);
    }
}
