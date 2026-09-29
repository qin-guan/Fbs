using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

/// <summary>Making an organisation, finding it by its address, and what only its admins can change.</summary>
public class TenantsTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static string NewSlug() => ClerkFbsApiFactory.NewSlug();

    private static string NewUser() => ClerkFbsApiFactory.NewUserId();

    private HttpClient ClientFor(string userId, string? name = "Some One") => Factory.ClientFor(userId, name);

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string slug, string name = "Test Org", string? timeZone = "Asia/Singapore") =>
        client.PostAsJsonAsync("/Tenants", new { name, slug, timeZone });

    private Task<Guid> AccountIdAsync(HttpClient client) => Factory.AccountIdOfAsync(client);

    /// <summary>Makes someone a member of an organisation the way an invite would, for tests that aren't about how they got in.</summary>
    private async Task JoinAsync(HttpClient client, string slug, MemberRole role = MemberRole.Member, MemberStatus status = MemberStatus.Active)
    {
        var accountId = await AccountIdAsync(client);
        var tenant = Factory.Db.Queryable<Tenant>().First(t => t.Slug == slug);
        Factory.Db.Insertable(
                new TenantMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserId = accountId,
                    DisplayName = "A Member",
                    Role = role,
                    Status = status,
                }
            )
            .ExecuteCommand();
    }

    [Test]
    public async Task Anyone_signed_in_can_make_an_organisation_and_are_its_admin()
    {
        var slug = NewSlug();
        using var client = ClientFor(NewUser(), "Founder");

        var response = await CreateAsync(client, slug, "Founders' Club", "Europe/London");

        await Assert.That(response).HasStatus(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(created.GetProperty("slug").GetString()).IsEqualTo(slug);
        await Assert.That(created.GetProperty("timeZone").GetString()).IsEqualTo("Europe/London");

        var me = await client.GetFromJsonAsync<JsonElement>("/Me");
        var membership = me.GetProperty("memberships").EnumerateArray().Single();
        await Assert.That(membership.GetProperty("tenantSlug").GetString()).IsEqualTo(slug);
        await Assert.That(membership.GetProperty("role").GetString()).IsEqualTo("Admin");
        await Assert.That(membership.GetProperty("status").GetString()).IsEqualTo("Active");
        await Assert.That(membership.GetProperty("displayName").GetString()).IsEqualTo("Founder");

        var stored = Factory.Db.Queryable<Tenant>().First(t => t.Slug == slug);
        await Assert.That(stored.Name).IsEqualTo("Founders' Club");
        await Assert.That(stored.RequireApproval).IsTrue();
        await Assert.That(stored.Status).IsEqualTo(TenantStatus.Active);
    }

    [Test]
    public async Task Without_signing_in_nothing_is_made()
    {
        using var client = Factory.CreateClient();
        var slug = NewSlug();

        await Assert.That(await CreateAsync(client, slug)).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(Factory.Db.Queryable<Tenant>().Any(t => t.Slug == slug)).IsFalse();
    }

    [Test]
    [Arguments("UPPER-case")]
    [Arguments("ab")]
    [Arguments("-leading")]
    [Arguments("trailing-")]
    [Arguments("double--hyphen")]
    [Arguments("has space")]
    [Arguments("under_score")]
    [Arguments("ünïcode")]
    [Arguments("")]
    public async Task An_address_that_is_not_lower_case_letters_digits_and_hyphens_is_refused(string slug)
    {
        using var client = ClientFor(NewUser());

        await Assert.That(await CreateAsync(client, slug)).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(Factory.Db.Queryable<Tenant>().Count(t => t.CreatedByUserId != null && t.Slug == slug)).IsEqualTo(0);
    }

    [Test]
    [Arguments("api")]
    [Arguments("admin")]
    [Arguments("tenants")]
    [Arguments("www")]
    public async Task An_address_that_means_something_in_the_app_is_refused(string slug)
    {
        using var client = ClientFor(NewUser());

        var response = await CreateAsync(client, slug);

        await Assert.That(response).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("slug-reserved");
    }

    [Test]
    public async Task A_name_or_time_zone_that_will_not_do_is_refused()
    {
        using var client = ClientFor(NewUser());

        await Assert.That(await CreateAsync(client, NewSlug(), name: "x")).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await CreateAsync(client, NewSlug(), name: new string('x', 101))).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await CreateAsync(client, NewSlug(), timeZone: "Mars/Olympus_Mons")).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await client.PostAsJsonAsync("/Tenants", new { name = "Fine", slug = NewSlug(), defaultCountryCode = "+65" })).HasStatus(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task An_address_that_is_taken_is_a_conflict_and_the_organisation_that_has_it_is_left_alone()
    {
        using var first = ClientFor(NewUser());
        using var second = ClientFor(NewUser());
        var slug = NewSlug();
        (await CreateAsync(first, slug, "First")).EnsureSuccessStatusCode();

        var response = await CreateAsync(second, slug, "Second");

        await Assert.That(response).HasStatus(HttpStatusCode.Conflict);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("slug-taken");
        await Assert.That(Factory.Db.Queryable<Tenant>().First(t => t.Slug == slug).Name).IsEqualTo("First");
        var me = await second.GetFromJsonAsync<JsonElement>("/Me");
        await Assert.That(me.GetProperty("memberships").GetArrayLength()).IsEqualTo(0);
        // And one that was there before, such as a tenant carried over
        await Assert.That(await CreateAsync(second, Factory.Slug)).HasStatus(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Several_at_once_for_the_same_address_make_one_and_the_others_are_told_it_is_taken()
    {
        var slug = NewSlug();
        var clients = Enumerable.Range(0, 8).Select(_ => ClientFor(NewUser())).ToList();
        try
        {
            List<Task<HttpResponseMessage>> requests;
            using (ExecutionContext.SuppressFlow())
            {
                requests = clients.Select(c => Task.Run(() => CreateAsync(c, slug))).ToList();
            }

            var statuses = (await Task.WhenAll(requests)).Select(r => r.StatusCode).ToList();

            await Assert.That(statuses.Count(s => s == HttpStatusCode.Created)).IsEqualTo(1);
            await Assert.That(statuses.Count(s => s == HttpStatusCode.Conflict)).IsEqualTo(7);
            await Assert.That(Factory.Db.Queryable<Tenant>().Count(t => t.Slug == slug)).IsEqualTo(1);
            // Whoever won is the only one of them who is a member of it
            var tenantId = Factory.Db.Queryable<Tenant>().First(t => t.Slug == slug).Id;
            await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == tenantId)).IsEqualTo(1);
        }
        finally
        {
            clients.ForEach(c => c.Dispose());
        }
    }

    [Test]
    public async Task Nobody_can_make_more_than_the_limit_but_it_is_theirs_alone()
    {
        using var busy = ClientFor(NewUser());
        using var other = ClientFor(NewUser());
        for (var i = 0; i < 3; i++)
        {
            (await CreateAsync(busy, NewSlug())).EnsureSuccessStatusCode();
        }

        var slug = NewSlug();
        var response = await CreateAsync(busy, slug);

        await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("tenant-limit");
        await Assert.That(Factory.Db.Queryable<Tenant>().Any(t => t.Slug == slug)).IsFalse();
        await Assert.That(await CreateAsync(other, NewSlug())).HasStatus(HttpStatusCode.Created);
    }

    [Test]
    public async Task Requests_at_the_same_moment_from_one_person_cannot_go_over_the_limit_together()
    {
        var userId = NewUser();
        var clients = Enumerable.Range(0, 8).Select(_ => ClientFor(userId)).ToList();
        try
        {
            // The account has to exist before they are made at once, as that is what is being locked
            (await clients[0].GetAsync("/Me")).EnsureSuccessStatusCode();

            List<Task<HttpResponseMessage>> requests;
            using (ExecutionContext.SuppressFlow())
            {
                requests = clients.Select(c => Task.Run(() => CreateAsync(c, NewSlug()))).ToList();
            }

            var statuses = (await Task.WhenAll(requests)).Select(r => r.StatusCode).ToList();

            await Assert.That(statuses.Count(s => s == HttpStatusCode.Created)).IsEqualTo(3);
            await Assert.That(statuses.Count(s => s == HttpStatusCode.Forbidden)).IsEqualTo(5);
            var accountId = Factory.Db.Queryable<UserAccount>().First(a => a.ClerkUserId == userId).Id;
            await Assert.That(Factory.Db.Queryable<Tenant>().Count(t => t.CreatedByUserId == accountId)).IsEqualTo(3);
        }
        finally
        {
            clients.ForEach(c => c.Dispose());
        }
    }

    [Test]
    public async Task A_member_sees_the_organisation_and_who_they_are_in_it()
    {
        using var founder = ClientFor(NewUser(), "Founder");
        var slug = NewSlug();
        (await CreateAsync(founder, slug, "Founders' Club")).EnsureSuccessStatusCode();

        var response = await founder.GetAsync($"/t/{slug}");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var org = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(org.GetProperty("name").GetString()).IsEqualTo("Founders' Club");
        await Assert.That(org.GetProperty("slotMinutes").GetInt32()).IsEqualTo(30);
        var me = org.GetProperty("me");
        await Assert.That(me.GetProperty("role").GetString()).IsEqualTo("Admin");
        await Assert.That(me.GetProperty("displayName").GetString()).IsEqualTo("Founder");
    }

    [Test]
    public async Task Someone_who_is_not_a_member_is_told_there_is_no_such_organisation_the_same_as_when_there_is_not()
    {
        using var founder = ClientFor(NewUser());
        using var outsider = ClientFor(NewUser());
        var slug = NewSlug();
        (await CreateAsync(founder, slug)).EnsureSuccessStatusCode();

        var real = await outsider.GetAsync($"/t/{slug}");
        var missing = await outsider.GetAsync($"/t/{NewSlug()}");

        await Assert.That(real).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(missing).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await real.Content.ReadAsStringAsync()).IsEqualTo(await missing.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Someone_who_has_left_is_told_the_same_and_someone_waiting_to_be_let_in_is_told_so()
    {
        using var founder = ClientFor(NewUser());
        using var gone = ClientFor(NewUser());
        using var waiting = ClientFor(NewUser());
        var slug = NewSlug();
        (await CreateAsync(founder, slug)).EnsureSuccessStatusCode();
        await JoinAsync(gone, slug, status: MemberStatus.Removed);
        await JoinAsync(waiting, slug, status: MemberStatus.Pending);

        await Assert.That(await gone.GetAsync($"/t/{slug}")).HasStatus(HttpStatusCode.NotFound);
        var response = await waiting.GetAsync($"/t/{slug}");
        await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("pending");
    }

    [Test]
    public async Task An_organisation_that_is_suspended_cannot_be_used_even_by_its_admin()
    {
        using var founder = ClientFor(NewUser());
        var slug = NewSlug();
        (await CreateAsync(founder, slug)).EnsureSuccessStatusCode();
        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { Status = TenantStatus.Suspended }).Where(t => t.Slug == slug).ExecuteCommand();

        await Assert.That(await founder.GetAsync($"/t/{slug}")).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await founder.GetAsync($"/t/{slug}/Settings")).HasStatus(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Without_signing_in_or_with_the_phone_number_sign_in_an_organisation_is_not_reachable()
    {
        using var founder = ClientFor(NewUser());
        var slug = NewSlug();
        (await CreateAsync(founder, slug)).EnsureSuccessStatusCode();
        using var none = Factory.CreateClient();
        using var phone = Factory.CreateClientFor(Users.Admin);

        await Assert.That(await none.GetAsync($"/t/{slug}")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await phone.GetAsync($"/t/{slug}")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Being_an_admin_of_one_organisation_gives_nothing_in_another()
    {
        using var first = ClientFor(NewUser());
        using var second = ClientFor(NewUser());
        var mine = NewSlug();
        var theirs = NewSlug();
        (await CreateAsync(first, mine)).EnsureSuccessStatusCode();
        (await CreateAsync(second, theirs, "Theirs")).EnsureSuccessStatusCode();

        await Assert.That(await first.GetAsync($"/t/{mine}/Settings")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await first.GetAsync($"/t/{theirs}")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await first.GetAsync($"/t/{theirs}/Settings")).HasStatus(HttpStatusCode.NotFound);
        var change = await first.PutAsJsonAsync($"/t/{theirs}/Settings", new { name = "Taken over", timeZone = "UTC", defaultCountryCode = "65", slotMinutes = 60, requireApproval = false });
        await Assert.That(change).HasStatus(HttpStatusCode.NotFound);
        var stored = Factory.Db.Queryable<Tenant>().First(t => t.Slug == theirs);
        await Assert.That(stored.Name).IsEqualTo("Theirs");
        await Assert.That(stored.RequireApproval).IsTrue();
    }

    [Test]
    public async Task An_admin_reads_and_changes_the_settings()
    {
        using var founder = ClientFor(NewUser());
        var slug = NewSlug();
        (await CreateAsync(founder, slug, "Before")).EnsureSuccessStatusCode();

        var response = await founder.PutAsJsonAsync(
            $"/t/{slug}/Settings",
            new { name = " After ", timeZone = "America/New_York", defaultCountryCode = "1", slotMinutes = 15, requireApproval = false }
        );

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var settings = await founder.GetFromJsonAsync<JsonElement>($"/t/{slug}/Settings");
        await Assert.That(settings.GetProperty("name").GetString()).IsEqualTo("After");
        await Assert.That(settings.GetProperty("timeZone").GetString()).IsEqualTo("America/New_York");
        await Assert.That(settings.GetProperty("defaultCountryCode").GetString()).IsEqualTo("1");
        await Assert.That(settings.GetProperty("slotMinutes").GetInt32()).IsEqualTo(15);
        await Assert.That(settings.GetProperty("requireApproval").GetBoolean()).IsFalse();
        var stored = Factory.Db.Queryable<Tenant>().First(t => t.Slug == slug);
        await Assert.That(stored.Name).IsEqualTo("After");
        await Assert.That(stored.Slug).IsEqualTo(slug);
        await Assert.That(stored.CreatedByUserId).IsNotNull();
    }

    [Test]
    public async Task Settings_that_will_not_do_are_refused_and_change_nothing()
    {
        using var founder = ClientFor(NewUser());
        var slug = NewSlug();
        (await CreateAsync(founder, slug, "Before")).EnsureSuccessStatusCode();
        var good = new { name = "After", timeZone = "UTC", defaultCountryCode = "65", slotMinutes = 30, requireApproval = true };

        var responses = new[]
        {
            await founder.PutAsJsonAsync($"/t/{slug}/Settings", good with { name = "x" }),
            await founder.PutAsJsonAsync($"/t/{slug}/Settings", good with { timeZone = "Nowhere/Land" }),
            await founder.PutAsJsonAsync($"/t/{slug}/Settings", good with { defaultCountryCode = "+65" }),
            await founder.PutAsJsonAsync($"/t/{slug}/Settings", good with { slotMinutes = 20 }),
            await founder.PutAsJsonAsync($"/t/{slug}/Settings", good with { slotMinutes = 0 }),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.BadRequest]);
        await Assert.That(Factory.Db.Queryable<Tenant>().First(t => t.Slug == slug).Name).IsEqualTo("Before");
    }

    [Test]
    public async Task A_member_who_is_not_an_admin_can_see_the_organisation_but_not_its_settings()
    {
        using var founder = ClientFor(NewUser());
        using var member = ClientFor(NewUser());
        var slug = NewSlug();
        (await CreateAsync(founder, slug, "Before")).EnsureSuccessStatusCode();
        await JoinAsync(member, slug);

        await Assert.That(await member.GetAsync($"/t/{slug}")).HasStatus(HttpStatusCode.OK);
        var read = await member.GetAsync($"/t/{slug}/Settings");
        await Assert.That(read).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await read.Content.ReadAsStringAsync()).Contains("admin-only");
        var change = await member.PutAsJsonAsync($"/t/{slug}/Settings", new { name = "Mine now", timeZone = "UTC", defaultCountryCode = "65", slotMinutes = 30, requireApproval = false });
        await Assert.That(change).HasStatus(HttpStatusCode.Forbidden);
        var stored = Factory.Db.Queryable<Tenant>().First(t => t.Slug == slug);
        await Assert.That(stored.Name).IsEqualTo("Before");
        await Assert.That(stored.RequireApproval).IsTrue();
    }
}
