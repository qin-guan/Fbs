using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;

namespace Fbs.WebApi.Tests;

/// <summary>Booking, changing and cancelling in an organisation, and looking at what has been booked.</summary>
public class TenantBookingsTests
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    /// <summary>A time in Singapore, a number of days from now, on the hour or the half hour.</summary>
    private static DateTimeOffset At(int daysAhead, int hour, int minute = 0)
    {
        var day = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore")).Date.AddDays(daysAhead);
        return new DateTimeOffset(day.AddHours(hour).AddMinutes(minute), Singapore);
    }

    private static object Slot(Guid facilityId, DateTimeOffset start, DateTimeOffset end) => new { facilityId, startDateTime = start, endDateTime = end };

    private static object Book(Guid facilityId, DateTimeOffset start, DateTimeOffset end, string conduct = "Lesson", string? description = null, string? pocName = null, string? pocPhone = null) =>
        new { conduct, description, pocName, pocPhone, slots = new[] { Slot(facilityId, start, end) } };

    private static async Task<Guid> BookAsync(HttpClient client, string slug, Guid facilityId, DateTimeOffset start, DateTimeOffset end, string conduct = "Lesson")
    {
        var response = await client.PostAsJsonAsync($"/t/{slug}/Bookings", Book(facilityId, start, end, conduct));
        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single().GetProperty("id").GetGuid();
    }

    private DataBooking Stored(Guid id) => Factory.Db.Queryable<DataBooking>().First(b => b.Id == id);

    private int Count(TestOrg org) => Factory.Db.Queryable<DataBooking>().Count(b => b.TenantId == org.TenantId);

    [Test]
    public async Task A_member_books_a_facility_and_it_is_kept_with_who_made_it_and_who_to_contact()
    {
        var org = await Factory.CreateOrgAsync();
        var unit = org.AddUnit("Alpha");
        var facility = org.AddFacility("Hall");
        var (client, memberId) = await org.AddMemberAsync("CPT Booker", unitId: unit);

        var response = await client.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(facility, At(3, 9), At(3, 11), " Lesson ", "Room 2", "LTA Contact", "+65 9123 4567"));

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        var booking = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single();
        await Assert.That(booking.GetProperty("facilityName").GetString()).IsEqualTo("Hall");
        await Assert.That(booking.GetProperty("conduct").GetString()).IsEqualTo("Lesson");
        await Assert.That(booking.GetProperty("bookedBy").GetProperty("displayName").GetString()).IsEqualTo("CPT Booker");
        await Assert.That(booking.GetProperty("canManage").GetBoolean()).IsTrue();
        await Assert.That(booking.GetProperty("startDateTime").GetDateTimeOffset()).IsEqualTo(At(3, 9));
        await Assert.That(booking.GetProperty("startDateTime").GetDateTimeOffset().Offset).IsEqualTo(Singapore);
        var stored = Stored(booking.GetProperty("id").GetGuid());
        await Assert.That(stored.BookedByMemberId).IsEqualTo(memberId);
        await Assert.That(stored.UnitId).IsEqualTo(unit);
        await Assert.That(stored.PocName).IsEqualTo("LTA Contact");
        await Assert.That(stored.PocPhone).IsEqualTo("+65 9123 4567");
        await Assert.That(stored.Description).IsEqualTo("Room 2");
        await Assert.That(stored.Revision).IsEqualTo(1);
        // Whoever else is told is told through the outbox, in the same transaction
        var message = Factory.Db.Queryable<OutboxMessage>().First(m => m.TenantId == org.TenantId && m.Type == "telegram.booking");
        await Assert.That(message.Payload).Contains(stored.Id.ToString());
    }

    [Test]
    public async Task Several_slots_are_booked_together_or_not_at_all()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var gym = org.AddFacility("Gym");
        var (client, _) = await org.AddMemberAsync();
        var taken = await BookAsync(org.Admin, org.Slug, gym, At(4, 10), At(4, 12));

        var clash = await client.PostAsJsonAsync(
            $"/t/{org.Slug}/Bookings",
            new { conduct = "Circuit", slots = new[] { Slot(hall, At(4, 9), At(4, 10)), Slot(gym, At(4, 11), At(4, 13)), Slot(hall, At(4, 9, 30), At(4, 10, 30)) } }
        );

        await Assert.That(clash).HasStatus(HttpStatusCode.Conflict);
        var body = await clash.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("slots[1]").And.Contains(taken.ToString());
        await Assert.That(body).Contains("slots[2]").And.Contains("slot 1");
        await Assert.That(Count(org)).IsEqualTo(1);

        var together = await client.PostAsJsonAsync(
            $"/t/{org.Slug}/Bookings",
            new { conduct = "Circuit", slots = new[] { Slot(hall, At(4, 9), At(4, 10)), Slot(gym, At(4, 12), At(4, 13)), Slot(hall, At(5, 9), At(5, 10)) } }
        );
        await Assert.That(together).HasStatus(HttpStatusCode.Created);
        await Assert.That(Count(org)).IsEqualTo(4);
        var batches = Factory.Db.Queryable<DataBooking>().Where(b => b.TenantId == org.TenantId && b.Conduct == "Circuit").Select(b => b.BatchId).ToList();
        await Assert.That(batches.Distinct().Count()).IsEqualTo(1);
        await Assert.That(batches[0]).IsNotNull();
    }

    [Test]
    public async Task A_booking_that_starts_as_another_ends_does_not_clash()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        await BookAsync(org.Admin, org.Slug, hall, At(2, 9), At(2, 10));

        await BookAsync(org.Admin, org.Slug, hall, At(2, 10), At(2, 11));
        await BookAsync(org.Admin, org.Slug, hall, At(2, 8), At(2, 9));

        await Assert.That(Count(org)).IsEqualTo(3);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, At(2, 9, 30), At(2, 10, 30)))).HasStatus(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Members_booking_the_same_time_at_once_get_it_once()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var members = new List<HttpClient>();
        for (var i = 0; i < 8; i++)
        {
            members.Add((await org.AddMemberAsync($"Member {i}")).Client);
        }

        List<Task<HttpResponseMessage>> requests;
        using (ExecutionContext.SuppressFlow())
        {
            requests = members.Select(m => Task.Run(() => m.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, At(6, 9), At(6, 10))))).ToList();
        }

        var statuses = (await Task.WhenAll(requests)).Select(r => r.StatusCode).ToList();

        await Assert.That(statuses.Count(s => s == HttpStatusCode.Created)).IsEqualTo(1);
        await Assert.That(statuses.Count(s => s == HttpStatusCode.Conflict)).IsEqualTo(7);
        await Assert.That(Count(org)).IsEqualTo(1);
    }

    [Test]
    public async Task Times_that_are_in_the_past_backwards_or_off_the_slot_are_refused()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");

        var past = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, At(-1, 9), At(-1, 10)));
        var backwards = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, At(3, 10), At(3, 9)));
        var off = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, At(3, 9, 10), At(3, 10)));

        await Assert.That(past).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await past.Content.ReadAsStringAsync()).Contains("start-past");
        await Assert.That(backwards).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await backwards.Content.ReadAsStringAsync()).Contains("end-before-start");
        await Assert.That(off).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await off.Content.ReadAsStringAsync()).Contains("not-on-slot");
        await Assert.That(Count(org)).IsEqualTo(0);
    }

    [Test]
    public async Task Slots_are_the_organisations_length_and_are_worked_out_in_its_time_zone()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        (await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Settings", new { name = "Test", timeZone = "Asia/Kolkata", defaultCountryCode = "91", slotMinutes = 60, requireApproval = true })).EnsureSuccessStatusCode();
        var ist = TimeSpan.FromHours(5.5);
        var tomorrow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).Date.AddDays(3);
        var nine = new DateTimeOffset(tomorrow.AddHours(9), ist);

        // On the hour in Kolkata, though half past in UTC
        var onTheHour = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, nine, nine.AddHours(1)));
        // On the hour in UTC, but not in Kolkata, and half an hour long
        var utcHour = nine.AddMinutes(30);
        var offTheHour = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, utcHour.ToUniversalTime(), utcHour.ToUniversalTime().AddHours(1)));
        var halfHour = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, nine.AddHours(2), nine.AddHours(2).AddMinutes(30)));

        await Assert.That(onTheHour).HasStatus(HttpStatusCode.Created);
        await Assert.That(offTheHour).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(halfHour).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(Count(org)).IsEqualTo(1);
    }

    [Test]
    public async Task What_is_written_on_a_booking_is_checked()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        object Make(string? conduct, string? description = null, string? pocName = null, string? pocPhone = null, int slots = 1) =>
            new { conduct, description, pocName, pocPhone, slots = Enumerable.Range(0, slots).Select(i => Slot(hall, At(10 + i, 9), At(10 + i, 10))).ToArray() };

        var responses = new[]
        {
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make(null)),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make("  ")),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make(new string('x', 101))),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make("Fine", new string('x', 2001))),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make("Fine", pocName: new string('x', 201))),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make("Fine", pocPhone: new string('1', 33))),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make("Fine", slots: 0)),
            await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make("Fine", slots: 51)),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.BadRequest]);
        await Assert.That(Count(org)).IsEqualTo(0);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Make("Fine", slots: 50))).HasStatus(HttpStatusCode.Created);
    }

    [Test]
    public async Task What_a_member_can_book_depends_on_their_unit_and_admins_can_book_anything()
    {
        var org = await Factory.CreateOrgAsync();
        var alpha = org.AddUnit("Alpha");
        var bravo = org.AddUnit("Bravo");
        var open = org.AddFacility("Open", availableToAll: true);
        var alphaOnly = org.AddFacility("Alpha only", availableToAll: false, alpha);
        var (inAlpha, _) = await org.AddMemberAsync("Alpha", unitId: alpha);
        var (inBravo, _) = await org.AddMemberAsync("Bravo", unitId: bravo);
        var (noUnit, _) = await org.AddMemberAsync("No unit");

        Task<HttpResponseMessage> TryAsync(HttpClient client, Guid facility, int day) => client.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(facility, At(day, 9), At(day, 10)));

        await Assert.That(await TryAsync(inAlpha, alphaOnly, 3)).HasStatus(HttpStatusCode.Created);
        var otherUnit = await TryAsync(inBravo, alphaOnly, 4);
        await Assert.That(otherUnit).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await otherUnit.Content.ReadAsStringAsync()).Contains("facility-forbidden");
        await Assert.That(await TryAsync(noUnit, alphaOnly, 4)).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await TryAsync(noUnit, open, 4)).HasStatus(HttpStatusCode.Created);
        await Assert.That(await TryAsync(org.Admin, alphaOnly, 5)).HasStatus(HttpStatusCode.Created);

        var listed = async (HttpClient c) => (await c.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Facilities/Bookable")).EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        await Assert.That(await listed(inAlpha)).IsEquivalentTo(["Alpha only", "Open"]);
        await Assert.That(await listed(inBravo)).IsEquivalentTo(["Open"]);
        await Assert.That(await listed(noUnit)).IsEquivalentTo(["Open"]);
        await Assert.That(await listed(org.Admin)).IsEquivalentTo(["Alpha only", "Open"]);
    }

    [Test]
    public async Task A_facility_of_another_organisation_cannot_be_booked_or_seen()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var theirs = other.AddFacility("Theirs");
        var theirBooking = await BookAsync(other.Admin, other.Slug, theirs, At(3, 9), At(3, 10));

        var response = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(theirs, At(3, 9), At(3, 10)));
        var listed = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Bookings?from={Uri.EscapeDataString(At(0, 0).ToString("O"))}&to={Uri.EscapeDataString(At(30, 0).ToString("O"))}");

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("facility-unknown");
        await Assert.That(Count(org)).IsEqualTo(0);
        await Assert.That(listed.GetArrayLength()).IsEqualTo(0);
        await Assert.That(await org.Admin.GetAsync($"/t/{org.Slug}/Bookings/{theirBooking}")).HasStatus(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Bookings_are_listed_for_a_window_earliest_first_and_only_those_that_share_time_with_it()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var gym = org.AddFacility("Gym");
        var (member, memberId) = await org.AddMemberAsync("Member");
        var before = await BookAsync(org.Admin, org.Slug, hall, At(2, 9), At(2, 10), "Before");
        var starts = await BookAsync(member, org.Slug, hall, At(4, 9), At(4, 11), "Starts inside");
        var overlaps = await BookAsync(org.Admin, org.Slug, gym, At(3, 22), At(4, 10), "Runs into it");
        var inside = await BookAsync(member, org.Slug, gym, At(5, 9), At(5, 10), "Inside");
        var after = await BookAsync(org.Admin, org.Slug, hall, At(9, 9), At(9, 10), "After");
        var cancelled = await BookAsync(org.Admin, org.Slug, hall, At(5, 12), At(5, 13), "Cancelled");
        (await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{cancelled}")).EnsureSuccessStatusCode();

        string Window(DateTimeOffset from, DateTimeOffset to, string more = "") => $"/t/{org.Slug}/Bookings?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}{more}";
        async Task<List<Guid>> IdsAsync(string url) => (await member.GetFromJsonAsync<JsonElement>(url)).EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();

        // From the evening before the third day, to the sixth, so that one is running when it starts
        var window = Window(At(3, 23), At(6, 0));
        await Assert.That(await IdsAsync(window)).IsEquivalentTo([overlaps, starts, inside]);
        var ids = await IdsAsync(window);
        await Assert.That(ids).IsEquivalentTo(new[] { overlaps, starts, inside }.ToList());
        await Assert.That(ids[0]).IsEqualTo(overlaps);
        await Assert.That(ids[1]).IsEqualTo(starts);
        await Assert.That(ids[2]).IsEqualTo(inside);
        await Assert.That(await IdsAsync(Window(At(3, 23), At(6, 0), $"&facilityId={hall}"))).IsEquivalentTo([starts]);
        await Assert.That(await IdsAsync(Window(At(3, 23), At(6, 0), "&mine=true"))).IsEquivalentTo([starts, inside]);
        await Assert.That(await IdsAsync(Window(At(3, 23), At(6, 0), $"&bookedBy={memberId}"))).IsEquivalentTo([starts, inside]);
        // One that ends as the window starts is not in it
        await Assert.That(await IdsAsync(Window(At(2, 10), At(2, 11)))).IsEmpty();
        await Assert.That(await IdsAsync(Window(At(0, 0), At(30, 0)))).DoesNotContain(cancelled);
        await Assert.That(await IdsAsync(Window(At(0, 0), At(30, 0)))).IsEquivalentTo([before, overlaps, starts, inside, after]);
    }

    [Test]
    public async Task With_no_window_it_is_from_the_start_of_today_and_a_window_that_is_too_large_or_backwards_is_refused()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var soon = await BookAsync(org.Admin, org.Slug, hall, At(5, 9), At(5, 10));
        var later = await BookAsync(org.Admin, org.Slug, hall, At(60, 9), At(60, 10));

        var defaults = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Bookings");
        var tooLarge = await org.Admin.GetAsync($"/t/{org.Slug}/Bookings?from={Uri.EscapeDataString(At(0, 0).ToString("O"))}&to={Uri.EscapeDataString(At(94, 0).ToString("O"))}");
        var backwards = await org.Admin.GetAsync($"/t/{org.Slug}/Bookings?from={Uri.EscapeDataString(At(5, 0).ToString("O"))}&to={Uri.EscapeDataString(At(4, 0).ToString("O"))}");
        var justEnough = await org.Admin.GetAsync($"/t/{org.Slug}/Bookings?from={Uri.EscapeDataString(At(0, 0).ToString("O"))}&to={Uri.EscapeDataString(At(93, 0).ToString("O"))}");

        await Assert.That(defaults.EnumerateArray().Select(b => b.GetProperty("id").GetGuid())).IsEquivalentTo([soon]);
        await Assert.That(tooLarge).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await tooLarge.Content.ReadAsStringAsync()).Contains("window-too-large");
        await Assert.That(backwards).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(justEnough).HasStatus(HttpStatusCode.OK);
        await Assert.That((await justEnough.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength()).IsEqualTo(2);
        await Assert.That(later).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task One_booking_is_read_by_any_member_and_a_cancelled_one_is_gone()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var (member, _) = await org.AddMemberAsync();
        var id = await BookAsync(org.Admin, org.Slug, hall, At(3, 9), At(3, 10));

        var read = await member.GetAsync($"/t/{org.Slug}/Bookings/{id}");
        (await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{id}")).EnsureSuccessStatusCode();

        await Assert.That(read).HasStatus(HttpStatusCode.OK);
        await Assert.That((await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canManage").GetBoolean()).IsFalse();
        await Assert.That(await member.GetAsync($"/t/{org.Slug}/Bookings/{id}")).HasStatus(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_booker_changes_what_a_booking_says_and_when_it_is_and_who_made_it_does_not_change()
    {
        var org = await Factory.CreateOrgAsync();
        var unit = org.AddUnit("Alpha");
        var hall = org.AddFacility("Hall");
        var (booker, bookerId) = await org.AddMemberAsync("Booker", unitId: unit);
        var (colleague, colleagueId) = await org.AddMemberAsync("Colleague", unitId: unit);
        var id = await BookAsync(booker, org.Slug, hall, At(3, 9), At(3, 10));

        var byBooker = await booker.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "Changed", description = "Room 3", pocName = "New PoC", pocPhone = "81234567", startDateTime = At(3, 10), endDateTime = At(3, 12) });
        await Assert.That(byBooker).HasStatus(HttpStatusCode.OK);
        var first = Stored(id);
        await Assert.That(first.Conduct).IsEqualTo("Changed");
        await Assert.That(first.PocPhone).IsEqualTo("81234567");
        await Assert.That(first.StartUtc).IsEqualTo(At(3, 10).ToUniversalTime());
        await Assert.That(first.Revision).IsEqualTo(2);
        await Assert.That(first.UpdatedByMemberId).IsEqualTo(bookerId);

        // Someone in the same unit can too, and it is still the booker's
        var byColleague = await colleague.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "By colleague" });
        await Assert.That(byColleague).HasStatus(HttpStatusCode.OK);
        var second = Stored(id);
        await Assert.That(second.BookedByMemberId).IsEqualTo(bookerId);
        await Assert.That(second.UpdatedByMemberId).IsEqualTo(colleagueId);
        await Assert.That(second.StartUtc).IsEqualTo(At(3, 10).ToUniversalTime());
        await Assert.That(second.FacilityId).IsEqualTo(hall);
        await Assert.That(second.Revision).IsEqualTo(3);
        var body = await byColleague.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("bookedBy").GetProperty("displayName").GetString()).IsEqualTo("Booker");
        await Assert.That(body.GetProperty("updatedBy").GetProperty("displayName").GetString()).IsEqualTo("Colleague");
        // Both changes were told
        await Assert.That(Factory.Db.Queryable<OutboxMessage>().Count(m => m.TenantId == org.TenantId && m.Type == "telegram.booking")).IsEqualTo(3);
    }

    [Test]
    public async Task Only_the_booker_their_unit_and_admins_can_change_or_cancel_a_booking()
    {
        var org = await Factory.CreateOrgAsync();
        var alpha = org.AddUnit("Alpha");
        var bravo = org.AddUnit("Bravo");
        var hall = org.AddFacility("Hall");
        var (booker, _) = await org.AddMemberAsync("Booker", unitId: alpha);
        var (outsider, _) = await org.AddMemberAsync("Outsider", unitId: bravo);
        var (noUnit, _) = await org.AddMemberAsync("No unit");
        var id = await BookAsync(booker, org.Slug, hall, At(3, 9), At(3, 10));

        foreach (var other in new[] { outsider, noUnit })
        {
            var change = await other.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "Not yours" });
            var cancel = await other.DeleteAsync($"/t/{org.Slug}/Bookings/{id}");
            await Assert.That(change).HasStatus(HttpStatusCode.Forbidden);
            await Assert.That(cancel).HasStatus(HttpStatusCode.Forbidden);
        }

        await Assert.That(Stored(id).Conduct).IsEqualTo("Lesson");
        await Assert.That(Stored(id).CancelledAt).IsNull();
        await Assert.That(await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "By admin" })).HasStatus(HttpStatusCode.OK);
        await Assert.That(await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{id}")).HasStatus(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Moving_a_booking_onto_another_is_refused_and_changes_nothing()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var first = await BookAsync(org.Admin, org.Slug, hall, At(3, 9), At(3, 10));
        var second = await BookAsync(org.Admin, org.Slug, hall, At(3, 11), At(3, 12));

        var response = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{second}", new { conduct = "Moved", startDateTime = At(3, 9, 30), endDateTime = At(3, 10, 30) });

        await Assert.That(response).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(first.ToString());
        await Assert.That(Stored(second).StartUtc).IsEqualTo(At(3, 11).ToUniversalTime());
        await Assert.That(Stored(second).Conduct).IsEqualTo("Lesson");
        // Nor can it move onto itself: a longer booking that overlaps only its own old time is fine
        await Assert.That(await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{second}", new { conduct = "Longer", startDateTime = At(3, 11), endDateTime = At(3, 13) })).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_booking_that_is_over_cannot_be_moved_and_one_underway_can_only_have_its_end_changed()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var now = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore"));
        var hour = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, Singapore);
        Guid Seed(DateTimeOffset start, DateTimeOffset end)
        {
            var id = Guid.NewGuid();
            Factory.Db.Insertable(
                    new DataBooking
                    {
                        Id = id,
                        TenantId = org.TenantId,
                        FacilityId = hall,
                        StartUtc = start.ToUniversalTime(),
                        EndUtc = end.ToUniversalTime(),
                        Conduct = "Lesson",
                        BookedByMemberId = Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId).Id,
                    }
                )
                .ExecuteCommand();
            return id;
        }

        var over = Seed(hour.AddHours(-3), hour.AddHours(-2));
        var underway = Seed(hour.AddHours(-1), hour.AddHours(3));

        var moveOver = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{over}", new { conduct = "Moved", startDateTime = hour.AddHours(5), endDateTime = hour.AddHours(6) });
        var moveStart = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{underway}", new { conduct = "Moved", startDateTime = hour.AddHours(1), endDateTime = hour.AddHours(3) });
        var endInPast = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{underway}", new { conduct = "Moved", startDateTime = hour.AddHours(-1), endDateTime = hour.AddHours(-1).AddMinutes(30) });
        var extend = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{underway}", new { conduct = "Extended", startDateTime = hour.AddHours(-1), endDateTime = hour.AddHours(4) });
        // What it says can still be changed without touching the time
        var rename = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{over}", new { conduct = "Renamed" });

        await Assert.That(moveOver).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await moveOver.Content.ReadAsStringAsync()).Contains("over");
        await Assert.That(moveStart).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await moveStart.Content.ReadAsStringAsync()).Contains("started");
        await Assert.That(endInPast).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(extend).HasStatus(HttpStatusCode.OK);
        await Assert.That(Stored(underway).EndUtc).IsEqualTo(hour.AddHours(4).ToUniversalTime());
        await Assert.That(rename).HasStatus(HttpStatusCode.OK);
        await Assert.That(Stored(over).StartUtc).IsEqualTo(hour.AddHours(-3).ToUniversalTime());
    }

    [Test]
    public async Task A_change_that_is_off_the_slot_or_has_only_one_time_is_refused()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var id = await BookAsync(org.Admin, org.Slug, hall, At(3, 9), At(3, 10));

        var off = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "Off", startDateTime = At(3, 9, 15), endDateTime = At(3, 10) });
        var half = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "Half", startDateTime = At(3, 9) });
        var noConduct = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "" });

        await Assert.That(off).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(half).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(noConduct).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(Stored(id).Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Cancelling_keeps_the_booking_frees_the_time_and_tells_who_did_it_and_a_second_time_finds_nothing()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var (member, memberId) = await org.AddMemberAsync();
        var id = await BookAsync(member, org.Slug, hall, At(3, 9), At(3, 10));
        var adminId = Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId && m.Role == MemberRole.Admin).Id;

        var cancel = await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{id}");
        var again = await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{id}");

        await Assert.That(cancel).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(again).HasStatus(HttpStatusCode.NotFound);
        var stored = Stored(id);
        await Assert.That(stored.CancelledAt).IsNotNull();
        await Assert.That(stored.CancelledByMemberId).IsEqualTo(adminId);
        await Assert.That(stored.BookedByMemberId).IsEqualTo(memberId);
        await Assert.That(Factory.Db.Queryable<OutboxMessage>().Count(m => m.TenantId == org.TenantId && m.Type == "telegram.booking")).IsEqualTo(2);
        await BookAsync(member, org.Slug, hall, At(3, 9), At(3, 10));
        await Assert.That(await member.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "Back" })).HasStatus(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Another_organisations_booking_is_not_found_to_read_change_or_cancel()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var theirs = await BookAsync(other.Admin, other.Slug, other.AddFacility("Theirs"), At(3, 9), At(3, 10));

        var responses = new[]
        {
            await org.Admin.GetAsync($"/t/{org.Slug}/Bookings/{theirs}"),
            await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{theirs}", new { conduct = "Taken" }),
            await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{theirs}"),
            // With the address of the organisation it is in, which they aren't
            await org.Admin.DeleteAsync($"/t/{other.Slug}/Bookings/{theirs}"),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.NotFound]);
        await Assert.That(Stored(theirs).CancelledAt).IsNull();
        await Assert.That(Stored(theirs).Conduct).IsEqualTo("Lesson");
    }

    [Test]
    public async Task Someone_waiting_to_be_let_in_cannot_book()
    {
        var org = await Factory.CreateOrgAsync();
        var hall = org.AddFacility("Hall");
        var (waiting, _) = await org.AddMemberAsync(status: MemberStatus.Pending);

        var response = await waiting.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(hall, At(3, 9), At(3, 10)));

        await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(Count(org)).IsEqualTo(0);
    }
}
