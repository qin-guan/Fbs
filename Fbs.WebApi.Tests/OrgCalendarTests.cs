using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;

namespace Fbs.WebApi.Tests;

/// <summary>Connecting an organization's Google Calendar from its settings, which takes a code read off the calendar.</summary>
public class OrgCalendarTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static string NewCalendarId() => $"cal-{Guid.NewGuid():N}";

    private static string CodeOn(ClerkFbsApiFactory factory, string calendarId)
    {
        var summary = factory.Google.Events(calendarId).Single(e => (e["summary"]?.GetValue<string>() ?? "").StartsWith(CalendarVerification.SummaryPrefix, StringComparison.Ordinal))["summary"]!.GetValue<string>();
        return summary[CalendarVerification.SummaryPrefix.Length..];
    }

    [Test]
    public async Task Nothing_is_connected_and_a_member_cannot_connect_one()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, _) = await org.AddMemberAsync();

        var body = await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Calendar");
        await Assert.That(body.GetProperty("status").GetString()).IsEqualTo("None");
        await Assert.That(body.GetProperty("calendarId").ValueKind).IsEqualTo(JsonValueKind.Null);

        await Assert.That(await member.GetAsync($"/t/{org.Slug}/Calendar")).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await member.PostAsJsonAsync($"/t/{org.Slug}/Calendar", new { calendarId = NewCalendarId() })).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await member.PostAsJsonAsync($"/t/{org.Slug}/Calendar/Confirm", new { code = "anything" })).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await member.DeleteAsync($"/t/{org.Slug}/Calendar")).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(Factory.Db.Queryable<CalendarConnection>().Any(c => c.TenantId == org.TenantId)).IsFalse();
    }

    [Test]
    public async Task Asking_to_connect_writes_a_code_into_the_calendar_and_does_not_send_the_code_back()
    {
        var org = await Factory.CreateOrgAsync();
        var calendarId = NewCalendarId();

        var response = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar", new { calendarId });
        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;
        var code = CodeOn(Factory, calendarId);

        await Assert.That(body.GetProperty("status").GetString()).IsEqualTo("Pending");
        await Assert.That(body.GetProperty("calendarId").GetString()).IsEqualTo(calendarId);
        await Assert.That(body.GetProperty("verificationExpiresAt").ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(text.Contains(code, StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Length).IsEqualTo(8);

        var stored = Factory.Db.Queryable<CalendarConnection>().Single(c => c.TenantId == org.TenantId);
        await Assert.That(stored.Status).IsEqualTo(CalendarConnectionStatus.Pending);
        await Assert.That(stored.VerificationCodeHash).IsNotEqualTo(code);
        await Assert.That(CalendarVerification.Matches(stored.VerificationCodeHash, code)).IsTrue();
        await Assert.That(Factory.Db.Queryable<AuditEntry>().Any(a => a.TenantId == org.TenantId && a.Action == "calendar.started")).IsTrue();
    }

    [Test]
    public async Task The_code_from_the_calendar_connects_it_and_bookings_are_queued()
    {
        var org = await Factory.CreateOrgAsync();
        var calendarId = NewCalendarId();
        var memberId = Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId).Id;
        var bookingId = Guid.NewGuid();
        Factory.Db.Insertable(new DataBooking
        {
            Id = bookingId,
            TenantId = org.TenantId,
            FacilityId = org.AddFacility("Hall"),
            StartUtc = DateTimeOffset.UtcNow.AddHours(1),
            EndUtc = DateTimeOffset.UtcNow.AddHours(2),
            Conduct = "Parade",
            BookedByMemberId = memberId,
        }).ExecuteCommand();

        (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar", new { calendarId })).EnsureSuccessStatusCode();
        var code = CodeOn(Factory, calendarId);
        var confirmed = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar/Confirm", new { code = code.ToUpperInvariant() });
        await Assert.That(confirmed).HasStatus(HttpStatusCode.OK);
        await Assert.That((await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()).IsEqualTo("Active");

        var stored = Factory.Db.Queryable<CalendarConnection>().Single(c => c.TenantId == org.TenantId);
        await Assert.That(stored.Status).IsEqualTo(CalendarConnectionStatus.Active);
        await Assert.That(stored.VerificationCodeHash).IsNull();
        await Assert.That(Factory.Google.Events(calendarId).Any(e => e["id"]?.GetValue<string>() == CalendarVerification.EventId(org.TenantId))).IsFalse();
        await Assert.That(Factory.Db.Queryable<OutboxMessage>().Count(m => m.TenantId == org.TenantId && m.Type == CalendarOutbox.MessageType)).IsGreaterThan(0);
        await Assert.That(Factory.Db.Queryable<AuditEntry>().Any(a => a.TenantId == org.TenantId && a.Action == "calendar.connected")).IsTrue();

        var again = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar", new { calendarId = NewCalendarId() });
        await Assert.That(again).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await again.Content.ReadAsStringAsync()).Contains("calendar-connected");
    }

    [Test]
    public async Task A_wrong_code_or_one_that_has_run_out_does_not_connect_it()
    {
        var org = await Factory.CreateOrgAsync();
        var calendarId = NewCalendarId();
        (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar", new { calendarId })).EnsureSuccessStatusCode();

        var wrong = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar/Confirm", new { code = "not-the-code" });
        await Assert.That(wrong).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await wrong.Content.ReadAsStringAsync()).Contains("code-wrong");
        await Assert.That(Factory.Db.Queryable<CalendarConnection>().Single(c => c.TenantId == org.TenantId).Status).IsEqualTo(CalendarConnectionStatus.Pending);

        Factory.Db.Updateable<CalendarConnection>()
            .SetColumns(c => new CalendarConnection { VerificationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) })
            .Where(c => c.TenantId == org.TenantId)
            .ExecuteCommand();
        var expired = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar/Confirm", new { code = CodeOn(Factory, calendarId) });
        await Assert.That(expired).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await expired.Content.ReadAsStringAsync()).Contains("code-wrong");
        await Assert.That(Factory.Db.Queryable<CalendarConnection>().Single(c => c.TenantId == org.TenantId).Status).IsEqualTo(CalendarConnectionStatus.Pending);

        var none = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar/Confirm", new { code = " " });
        await Assert.That(none).HasStatus(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task A_calendar_another_organization_has_is_refused_until_they_stop()
    {
        var first = await Factory.CreateOrgAsync();
        var second = await Factory.CreateOrgAsync();
        var calendarId = NewCalendarId();
        (await first.Admin.PostAsJsonAsync($"/t/{first.Slug}/Calendar", new { calendarId })).EnsureSuccessStatusCode();

        var taken = await second.Admin.PostAsJsonAsync($"/t/{second.Slug}/Calendar", new { calendarId });
        await Assert.That(taken).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await taken.Content.ReadAsStringAsync()).Contains("calendar-taken");
        await Assert.That(Factory.Db.Queryable<CalendarConnection>().Any(c => c.TenantId == second.TenantId)).IsFalse();

        await Assert.That(await first.Admin.DeleteAsync($"/t/{first.Slug}/Calendar")).HasStatus(HttpStatusCode.NoContent);
        await Assert.That((await first.Admin.GetFromJsonAsync<JsonElement>($"/t/{first.Slug}/Calendar")).GetProperty("status").GetString()).IsEqualTo("None");
        await Assert.That(Factory.Google.Events(calendarId)).IsEmpty();

        var freed = await second.Admin.PostAsJsonAsync($"/t/{second.Slug}/Calendar", new { calendarId });
        await Assert.That(freed).HasStatus(HttpStatusCode.OK);
        await Assert.That(Factory.Db.Queryable<AuditEntry>().Any(a => a.TenantId == first.TenantId && a.Action == "calendar.disconnected")).IsTrue();
    }

    [Test]
    public async Task What_Google_refuses_or_what_is_not_a_calendar_id_is_not_connected()
    {
        var org = await Factory.CreateOrgAsync();
        var calendarId = NewCalendarId();
        var primary = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar", new { calendarId = "primary" });
        await Assert.That(primary).HasStatus(HttpStatusCode.BadRequest);

        Factory.Google.ForbiddenCalendars.Add(calendarId);
        try
        {
            var refused = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar", new { calendarId });
            await Assert.That(refused).HasStatus(HttpStatusCode.BadRequest);
            await Assert.That(await refused.Content.ReadAsStringAsync()).Contains("calendar-refused");
        }
        finally
        {
            Factory.Google.ForbiddenCalendars.Remove(calendarId);
        }

        var limitedId = NewCalendarId();
        Factory.Google.RateLimitedCalendars.Add(limitedId);
        try
        {
            var limited = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Calendar", new { calendarId = limitedId });
            await Assert.That(limited).HasStatus(HttpStatusCode.ServiceUnavailable);
            await Assert.That(await limited.Content.ReadAsStringAsync()).Contains("calendar-unavailable");
        }
        finally
        {
            Factory.Google.RateLimitedCalendars.Remove(limitedId);
        }

        await Assert.That(Factory.Db.Queryable<CalendarConnection>().Any(c => c.TenantId == org.TenantId)).IsFalse();
    }
}