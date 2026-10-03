using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Telemetry;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Tests;

/// <summary>
/// What is counted and timed for whoever looks after the system, seen the way an exporter sees it: that the things worth
/// watching are counted when they happen, only when they happen, and by tags that are ours and few.
/// </summary>
public class MetricsTests
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    [ClassDataSource<MeteredFactory>]
    public required MeteredFactory Factory { get; init; }

    /// <summary>One unit an organisation, and one organisation an hour each.</summary>
    public class MeteredFactory : ClerkFbsApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Limits:MaxUnits", "1");
            builder.UseSetting("RateLimits:Limits:create-organization:PermitLimit", "1");
            builder.UseSetting("RateLimits:Limits:create-organization:WindowSeconds", "3600");
        }
    }

    private static DateTimeOffset At(int daysAhead, int hour)
    {
        var day = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore")).Date.AddDays(daysAhead);
        return new DateTimeOffset(day.AddHours(hour), Singapore);
    }

    private static object Book(Guid facilityId, DateTimeOffset start, DateTimeOffset end) =>
        new { conduct = "Lesson", slots = new[] { new { facilityId, startDateTime = start, endDateTime = end } } };

    private HttpClient WithToken(string? token)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    [Test]
    public async Task Signing_in_for_the_first_time_and_making_an_organisation_are_counted_and_being_refused_is_not()
    {
        using var metrics = new MetricsRecorder();

        var org = await Factory.CreateOrgAsync();

        await Assert.That(metrics.Sum("fbs.accounts.created")).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.tenants.created")).IsEqualTo(1);

        // Somebody else, wanting the same address
        var other = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        var taken = await other.PostAsJsonAsync("/Tenants", new { name = "Again", slug = org.Slug });

        await Assert.That(taken).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(metrics.Sum("fbs.accounts.created")).IsEqualTo(2);
        await Assert.That(metrics.Sum("fbs.tenants.created")).IsEqualTo(1);

        // Being somebody who is known already is not being new
        await other.GetAsync("/Me");
        await Assert.That(metrics.Sum("fbs.accounts.created")).IsEqualTo(2);
    }

    [Test]
    public async Task Using_an_invite_link_is_counted_by_what_came_of_it()
    {
        var org = await Factory.CreateOrgAsync();
        Task<HttpResponseMessage> RequireApproval(bool require) =>
            org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Settings", new { name = "Test Org", timeZone = "Asia/Singapore", defaultCountryCode = "65", slotMinutes = 30, requireApproval = require });
        (await RequireApproval(false)).EnsureSuccessStatusCode();

        async Task<string> InviteAsync(int maxUses)
        {
            var response = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { maxUses });
            await Assert.That(response).HasStatus(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        }

        var once = await InviteAsync(1);
        var first = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        var second = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        using var metrics = new MetricsRecorder();

        await Assert.That(await first.PostAsJsonAsync($"/Invites/{once}/Accept", new { })).HasStatus(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.invites.used", ("outcome", "joined"))).IsEqualTo(1);

        // In already, so nothing is used
        await Assert.That(await first.PostAsJsonAsync($"/Invites/{once}/Accept", new { })).HasStatus(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.invites.used", ("outcome", "already_in"))).IsEqualTo(1);

        // Somebody else, with the only use gone
        await Assert.That(await second.PostAsJsonAsync($"/Invites/{once}/Accept", new { })).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(metrics.Sum("fbs.invites.used", ("outcome", "unusable"))).IsEqualTo(1);

        // A link that was never made
        await Assert.That(await second.PostAsJsonAsync($"/Invites/{Guid.NewGuid():N}/Accept", new { })).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(metrics.Sum("fbs.invites.used", ("outcome", "not_found"))).IsEqualTo(1);

        // Waiting for an admin
        (await RequireApproval(true)).EnsureSuccessStatusCode();
        var approval = await InviteAsync(5);
        await Assert.That(await second.PostAsJsonAsync($"/Invites/{approval}/Accept", new { })).HasStatus(HttpStatusCode.OK);

        await Assert.That(metrics.Sum("fbs.invites.used", ("outcome", "waiting"))).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.invites.used")).IsEqualTo(5);
    }

    [Test]
    public async Task Bookings_made_changed_and_cancelled_are_counted_and_so_are_the_ones_refused_because_the_time_is_taken()
    {
        var org = await Factory.CreateOrgAsync();
        var facility = org.AddFacility("Hall");
        using var metrics = new MetricsRecorder();

        var made = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(facility, At(3, 9), At(3, 11)));
        await Assert.That(made).HasStatus(HttpStatusCode.Created);
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single().GetProperty("id").GetGuid();
        await Assert.That(metrics.Sum("fbs.bookings.made")).IsEqualTo(1);

        // Somebody else has it: not made, and said so
        var clash = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(facility, At(3, 10), At(3, 12)));
        await Assert.That(clash).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(metrics.Sum("fbs.bookings.made")).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.bookings.clashes")).IsEqualTo(1);

        // Moved onto the same time as another
        var other = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Bookings", Book(facility, At(4, 9), At(4, 11)));
        var otherId = (await other.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single().GetProperty("id").GetGuid();
        var moved = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{otherId}", new { conduct = "Moved", startDateTime = At(3, 9), endDateTime = At(3, 11) });
        await Assert.That(moved).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(metrics.Sum("fbs.bookings.clashes")).IsEqualTo(2);
        await Assert.That(metrics.Sum("fbs.bookings.changed")).IsEqualTo(0);

        await Assert.That(await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Bookings/{id}", new { conduct = "Changed" })).HasStatus(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.bookings.changed")).IsEqualTo(1);

        await Assert.That(await org.Admin.DeleteAsync($"/t/{org.Slug}/Bookings/{id}")).HasStatus(HttpStatusCode.NoContent);
        await Assert.That(metrics.Sum("fbs.bookings.cancelled")).IsEqualTo(1);

        // Every slot of a booking is a booking, as that is what is stored and what a limit counts
        var several = await org.Admin.PostAsJsonAsync(
            $"/t/{org.Slug}/Bookings",
            new
            {
                conduct = "Series",
                slots = new[] { 5, 6, 7 }.Select(days => new { facilityId = facility, startDateTime = At(days, 9), endDateTime = At(days, 10) }).ToArray(),
            }
        );
        await Assert.That(several).HasStatus(HttpStatusCode.Created);
        await Assert.That(metrics.Sum("fbs.bookings.made")).IsEqualTo(5);
    }

    [Test]
    public async Task An_organisation_at_a_limit_is_counted_by_which_limit()
    {
        var org = await Factory.CreateOrgAsync();
        using var metrics = new MetricsRecorder();

        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Alpha" })).HasStatus(HttpStatusCode.Created);
        await Assert.That(metrics.Sum("fbs.quota.refusals")).IsEqualTo(0);

        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Bravo" })).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Units", new { name = "Charlie" })).HasStatus(HttpStatusCode.Forbidden);

        await Assert.That(metrics.Sum("fbs.quota.refusals", ("limit", "unit-limit"))).IsEqualTo(2);
        await Assert.That(metrics.Sum("fbs.quota.refusals")).IsEqualTo(2);
    }

    [Test]
    public async Task Session_tokens_that_are_refused_are_counted_by_why_and_having_none_is_not_counted()
    {
        var clerk = Factory.Clerk;
        using var metrics = new MetricsRecorder();

        async Task RefusedAsync(string? token)
        {
            using var client = WithToken(token);
            await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        }

        await RefusedAsync(null);
        await Assert.That(metrics.Sum("fbs.auth.failures")).IsEqualTo(0);

        await RefusedAsync(clerk.Token("user_a", validFor: TimeSpan.FromMinutes(-5)));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("reason", "expired"))).IsEqualTo(1);

        await RefusedAsync(clerk.Token("user_b", signWith: ClerkTestIssuer.AnotherKey()));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("reason", "signature"))).IsEqualTo(1);

        // A key that isn't one of Clerk's
        await RefusedAsync(clerk.Token("user_c", signWith: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "somebody-elses" }));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("reason", "unknown_key"))).IsEqualTo(1);

        await RefusedAsync(clerk.Token("user_d", issuer: "https://someone-else.example"));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("reason", "issuer"))).IsEqualTo(1);

        await RefusedAsync(clerk.Token("user_e", authorizedParty: "https://another-app.example"));
        await RefusedAsync(clerk.Token("user_f", authorizedParty: null));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("reason", "azp"))).IsEqualTo(2);

        await RefusedAsync("not.a.token");
        await Assert.That(metrics.Sum("fbs.auth.failures", ("reason", "invalid"))).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.auth.failures")).IsEqualTo(7);

        // And one that is good is nothing to count
        using var good = WithToken(clerk.Token(ClerkFbsApiFactory.NewUserId()));
        await Assert.That(await good.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.auth.failures")).IsEqualTo(7);
    }

    [Test]
    public async Task Webhooks_are_counted_by_their_type_and_whether_they_were_accepted_and_what_is_sent_cannot_make_up_a_type()
    {
        var known = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Known");
        var accountId = await Factory.AccountIdOfAsync(known);
        var clerkUserId = Factory.Db.Queryable<UserAccount>().First(a => a.Id == accountId).ClerkUserId;
        using var metrics = new MetricsRecorder();

        async Task<HttpStatusCode> SendAsync(string body, string? secret = null)
        {
            using var client = Factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/clerk") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            foreach (var (name, value) in ClerkTestIssuer.WebhookHeaders($"msg_{Guid.NewGuid():N}", body, secret: secret))
            {
                request.Headers.Add(name, value);
            }

            return (await client.SendAsync(request)).StatusCode;
        }

        await Assert.That(await SendAsync("""{"type":"user.created","data":{"id":"user_x"}}""")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.webhooks.received", ("type", "user.created"), ("result", "accepted"))).IsEqualTo(1);

        // Whatever type it says, so long as it is signed, is one line
        await Assert.That(await SendAsync("""{"type":"whatever.somebody.made.up","data":{}}""")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await SendAsync("""{"type":"another.made.up.one","data":{}}""")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.webhooks.received", ("type", "other"), ("result", "accepted"))).IsEqualTo(2);

        // Not signed with our secret: not what it says it is, so its type is not counted
        var forged = "whsec_" + Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        await Assert.That(await SendAsync("""{"type":"user.deleted","data":{"id":"user_x"}}""", forged)).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(metrics.Sum("fbs.webhooks.received", ("type", "unknown"), ("result", "invalid_signature"))).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.accounts.erased")).IsEqualTo(0);

        // A deletion of somebody there is nothing of is accepted, and nothing was erased
        await Assert.That(await SendAsync("""{"type":"user.deleted","data":{"id":"user_never_seen"}}""")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.accounts.erased")).IsEqualTo(0);

        await Assert.That(await SendAsync($$$"""{"type":"user.deleted","data":{"id":"{{{clerkUserId}}}"}}""")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.accounts.erased")).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.webhooks.received", ("type", "user.deleted"), ("result", "accepted"))).IsEqualTo(2);

        // And once more: it is not erased twice
        await Assert.That(await SendAsync($$$"""{"type":"user.deleted","data":{"id":"{{{clerkUserId}}}"}}""")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.accounts.erased")).IsEqualTo(1);
    }

    [Test]
    public async Task Requests_refused_for_being_too_many_are_counted_by_which_limit()
    {
        var person = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        using var metrics = new MetricsRecorder();

        // Bad, so nothing is made, but it is a go
        await person.PostAsJsonAsync("/Tenants", new { name = "x", slug = "!" });
        await Assert.That(metrics.Sum("fbs.rate_limit.rejections")).IsEqualTo(0);

        var refused = await person.PostAsJsonAsync("/Tenants", new { name = "x", slug = "!" });

        await Assert.That(refused).HasStatus(HttpStatusCode.TooManyRequests);
        await Assert.That(metrics.Sum("fbs.rate_limit.rejections", ("policy", "create-organization"))).IsEqualTo(1);
    }

    [Test]
    public async Task Database_statements_are_timed_by_what_kind_they_are_and_the_ones_that_fail_are_counted()
    {
        using var metrics = new MetricsRecorder();

        await Factory.CreateOrgAsync();

        await Assert.That(metrics.Recorded("fbs.db.query.duration", ("operation", "select"))).IsGreaterThan(0);
        await Assert.That(metrics.Recorded("fbs.db.query.duration", ("operation", "insert"))).IsGreaterThan(0);
        await Assert.That(metrics.Of("fbs.db.query.duration").All(m => m.Value >= 0 && m.Value < 60)).IsTrue();
        await Assert.That(metrics.Sum("fbs.db.query.errors")).IsEqualTo(0);

        // What the table is called is not in what is counted
        await Assert.That(() => Factory.Db.Ado.ExecuteCommand("SELECT * FROM ThereIsNoSuchTable")).Throws<Exception>();
        await Assert.That(metrics.Sum("fbs.db.query.errors", ("operation", "select"))).IsEqualTo(1);
        await Assert.That(() => Factory.Db.Ado.ExecuteCommand("DROP TABLE ThereIsNoSuchTable")).Throws<Exception>();
        await Assert.That(metrics.Sum("fbs.db.query.errors", ("operation", "other"))).IsEqualTo(1);
        await Assert.That(metrics.Of("fbs.db.query.errors").SelectMany(m => m.Tags.Values).Any(v => v!.Contains("ThereIsNoSuchTable"))).IsFalse();
    }

    [Test]
    public async Task What_is_waiting_and_how_many_there_are_is_read_from_the_database_for_the_gauges()
    {
        using var metrics = new MetricsRecorder();
        var db = Factory.Db;
        var type = $"test.gauges.{Guid.NewGuid():N}";

        async Task<(Dictionary<string, double> Tenants, Dictionary<string, double> Members, Dictionary<string, double> Calendars, double Held)> LookAsync()
        {
            await DatabaseGauges.RefreshAsync(db, CancellationToken.None);
            return (metrics.Observe("fbs.tenants", "status"), metrics.Observe("fbs.members", "status"), metrics.Observe("fbs.calendar.connections", "status"), metrics.Observe("fbs.outbox.held") ?? 0);
        }

        var before = await LookAsync();
        // Every state is there, even those with none, so a line doesn't disappear
        await Assert.That(before.Tenants.Keys).IsEquivalentTo(["Active", "Suspended", "PendingDeletion"]);
        await Assert.That(before.Calendars.Keys).IsEquivalentTo(Enum.GetNames<CalendarConnectionStatus>());

        var active = await Factory.CreateOrgAsync();
        var suspended = await Factory.CreateOrgAsync();
        db.Updateable<Tenant>().SetColumns(t => new Tenant { Status = TenantStatus.Suspended }).Where(t => t.Id == suspended.TenantId).ExecuteCommand();
        db.Insertable(new CalendarConnection { Id = Guid.NewGuid(), TenantId = active.TenantId, CalendarId = "somebody@example.com", Status = CalendarConnectionStatus.Failed }).ExecuteCommand();

        // Two waiting for an active organisation, one of which is late; one for a suspended one; and two that were given up on
        await OutboxWriter.EnqueueAsync(db, active.TenantId, type, new { });
        var late = await OutboxWriter.EnqueueAsync(db, active.TenantId, type, new { });
        db.Updateable<OutboxMessage>().SetColumns(m => new OutboxMessage { NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-2) }).Where(m => m.Id == late).ExecuteCommand();
        await OutboxWriter.EnqueueAsync(db, suspended.TenantId, type, new { });
        foreach (var _ in new[] { 1, 2 })
        {
            var dead = await OutboxWriter.EnqueueAsync(db, active.TenantId, type, new { });
            db.Updateable<OutboxMessage>().SetColumns(m => new OutboxMessage { Status = OutboxStatus.Dead }).Where(m => m.Id == dead).ExecuteCommand();
        }

        var after = await LookAsync();

        await Assert.That(after.Tenants["Active"] - before.Tenants["Active"]).IsEqualTo(1);
        await Assert.That(after.Tenants["Suspended"] - before.Tenants["Suspended"]).IsEqualTo(1);
        await Assert.That(after.Members["Active"] - before.Members["Active"]).IsEqualTo(2);
        await Assert.That(after.Calendars["Failed"] - before.Calendars["Failed"]).IsEqualTo(1);
        await Assert.That(after.Held - before.Held).IsEqualTo(1);
        // What is held for a suspended organisation is not late, and what was given up on is not waiting
        await Assert.That(metrics.Observe("fbs.outbox.pending", "type")[type]).IsEqualTo(2);
        await Assert.That(metrics.Observe("fbs.outbox.dead", "type")[type]).IsEqualTo(2);
        var age = metrics.Observe("fbs.outbox.oldest_due.age", "type")[type];
        await Assert.That(age).IsGreaterThanOrEqualTo(120);
        await Assert.That(age).IsLessThan(300);
    }

    [Test]
    public async Task The_gauges_keep_the_last_look_when_the_database_cannot_be_reached()
    {
        using var metrics = new MetricsRecorder();
        await DatabaseGauges.RefreshAsync(Factory.Db, CancellationToken.None);
        var before = metrics.Observe("fbs.tenants", "status");

        var unreachable = Fbs.WebApi.Data.SqlSugarClientFactory.Create("Server=127.0.0.1;Port=1;Database=none;User=none;Password=none;Connect Timeout=1");
        await Assert.That(async () => await DatabaseGauges.RefreshAsync(unreachable, CancellationToken.None)).Throws<Exception>();

        await Assert.That(metrics.Observe("fbs.tenants", "status")).IsEquivalentTo(before);
    }
}
