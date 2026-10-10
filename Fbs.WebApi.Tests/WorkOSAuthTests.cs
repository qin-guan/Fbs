using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Signing in with an access token from WorkOS, while Clerk is still on, as it is while people move: somebody who had a Clerk
/// account is the same account with WorkOS, with everything they had.
/// </summary>
public class WorkOSAuthTests
{
    [ClassDataSource<MovingFbsApiFactory>]
    public required MovingFbsApiFactory Factory { get; init; }

    private WorkOSTestIssuer WorkOS => Factory.WorkOS;

    private UserAccount? ByWorkOS(string workOSUserId) => Factory.Db.Queryable<UserAccount>().First(a => a.WorkOSUserId == workOSUserId);

    private UserAccount ByClerk(string clerkUserId) => Factory.Db.Queryable<UserAccount>().First(a => a.ClerkUserId == clerkUserId);

    [Test]
    public async Task An_access_token_from_workos_signs_in_and_makes_an_account_from_what_its_template_adds()
    {
        var userId = WorkOSTestIssuer.NewUserId();
        using var client = Factory.ClientWithWorkOS(WorkOS.Token(userId, "new@example.com", "New", "Person"));

        var response = await client.GetAsync("/Me");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(me.GetProperty("name").GetString()).IsEqualTo("New Person");
        await Assert.That(me.GetProperty("email").GetString()).IsEqualTo("new@example.com");
        var account = ByWorkOS(userId)!;
        await Assert.That(account.Id).IsEqualTo(me.GetProperty("id").GetGuid());
        await Assert.That(account.ClerkUserId).IsNull();
    }

    [Test]
    public async Task Without_names_in_the_token_it_still_signs_in()
    {
        var userId = WorkOSTestIssuer.NewUserId();
        using var client = Factory.ClientWithWorkOS(WorkOS.Token(userId, email: null, givenName: null, familyName: null));

        var me = await client.GetFromJsonAsync<JsonElement>("/Me");

        await Assert.That(me.GetProperty("name").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(ByWorkOS(userId)).IsNotNull();
    }

    [Test]
    public async Task Somebody_moved_from_clerk_is_the_account_they_had_with_everything_in_it()
    {
        var org = await Factory.CreateOrgAsync(founderName: "Mover");
        var accountId = await Factory.AccountIdOfAsync(org.Admin);
        var clerkUserId = Factory.Db.Queryable<UserAccount>().First(a => a.Id == accountId).ClerkUserId!;
        var before = await org.Admin.GetFromJsonAsync<JsonElement>("/Me");
        using var metrics = new MetricsRecorder();

        var workOSUserId = WorkOSTestIssuer.NewUserId();
        using var moved = Factory.ClientWithWorkOS(WorkOS.Token(workOSUserId, clerkUserId: clerkUserId));
        var after = await moved.GetFromJsonAsync<JsonElement>("/Me");

        await Assert.That(after.GetProperty("id").GetGuid()).IsEqualTo(before.GetProperty("id").GetGuid());
        var membership = after.GetProperty("memberships").EnumerateArray().Single();
        await Assert.That(membership.GetProperty("tenantSlug").GetString()).IsEqualTo(org.Slug);
        await Assert.That(membership.GetProperty("role").GetString()).IsEqualTo("Admin");
        // And what is only for admins of it, as they still are
        await Assert.That(await moved.GetAsync($"/t/{org.Slug}/Settings")).HasStatus(HttpStatusCode.OK);
        await Assert.That(ByClerk(clerkUserId).WorkOSUserId).IsEqualTo(workOSUserId);
        await Assert.That(metrics.Sum("fbs.accounts.moved", ("when", "sign_in"))).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.accounts.created")).IsEqualTo(0);

        // Their Clerk session, in a tab that is still open, is the same account too, until Clerk is turned off
        await Assert.That((await org.Admin.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid()).IsEqualTo(before.GetProperty("id").GetGuid());
        // And the next time with WorkOS it is found by its WorkOS ID, with nothing joined again
        using var again = Factory.ClientWithWorkOS(WorkOS.Token(workOSUserId));
        await Assert.That((await again.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid()).IsEqualTo(before.GetProperty("id").GetGuid());
        await Assert.That(metrics.Sum("fbs.accounts.moved")).IsEqualTo(1);
    }

    [Test]
    public async Task Somebody_the_import_already_joined_is_their_account_without_anything_in_the_token()
    {
        var clerkUserId = ClerkFbsApiFactory.NewUserId();
        var accountId = await Factory.AccountIdOfAsync(Factory.ClientFor(clerkUserId));
        var workOSUserId = WorkOSTestIssuer.NewUserId();
        Factory.Db.Updateable<UserAccount>().SetColumns(a => new UserAccount { WorkOSUserId = workOSUserId }).Where(a => a.Id == accountId).ExecuteCommand();

        using var client = Factory.ClientWithWorkOS(WorkOS.Token(workOSUserId));

        await Assert.That((await client.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid()).IsEqualTo(accountId);
    }

    [Test]
    public async Task An_account_already_joined_to_one_workos_user_is_not_taken_by_another_that_names_it()
    {
        var clerkUserId = ClerkFbsApiFactory.NewUserId();
        var accountId = await Factory.AccountIdOfAsync(Factory.ClientFor(clerkUserId));
        var first = WorkOSTestIssuer.NewUserId();
        using var owner = Factory.ClientWithWorkOS(WorkOS.Token(first, clerkUserId: clerkUserId));
        (await owner.GetAsync("/Me")).EnsureSuccessStatusCode();

        var second = WorkOSTestIssuer.NewUserId();
        using var other = Factory.ClientWithWorkOS(WorkOS.Token(second, clerkUserId: clerkUserId));
        var me = await other.GetFromJsonAsync<JsonElement>("/Me");

        await Assert.That(me.GetProperty("id").GetGuid()).IsNotEqualTo(accountId);
        await Assert.That(ByClerk(clerkUserId).WorkOSUserId).IsEqualTo(first);
        await Assert.That(ByWorkOS(second)!.ClerkUserId).IsNull();
    }

    [Test]
    public async Task An_account_that_was_erased_is_not_joined_and_whoever_signs_in_starts_again()
    {
        var clerkUserId = ClerkFbsApiFactory.NewUserId();
        var accountId = await Factory.AccountIdOfAsync(Factory.ClientFor(clerkUserId));
        Factory.Db.Updateable<UserAccount>().SetColumns(a => new UserAccount { DeletedAt = DateTimeOffset.UtcNow }).Where(a => a.Id == accountId).ExecuteCommand();

        var workOSUserId = WorkOSTestIssuer.NewUserId();
        using var client = Factory.ClientWithWorkOS(WorkOS.Token(workOSUserId, clerkUserId: clerkUserId));
        var response = await client.GetAsync("/Me");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(me.GetProperty("id").GetGuid()).IsNotEqualTo(accountId);
        await Assert.That(me.GetProperty("memberships").GetArrayLength()).IsEqualTo(0);
        await Assert.That(ByClerk(clerkUserId).WorkOSUserId).IsNull();
    }

    [Test]
    public async Task Requests_at_the_same_moment_from_somebody_moving_join_their_account_once()
    {
        for (var person = 0; person < 10; person++)
        {
            var clerkUserId = ClerkFbsApiFactory.NewUserId();
            var accountId = await Factory.AccountIdOfAsync(Factory.ClientFor(clerkUserId));
            var token = WorkOS.Token(WorkOSTestIssuer.NewUserId(), clerkUserId: clerkUserId);

            List<Task<HttpResponseMessage>> requests;
            using (ExecutionContext.SuppressFlow())
            {
                requests = Enumerable
                    .Range(0, 6)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            using var client = Factory.ClientWithWorkOS(token);
                            return await client.GetAsync("/Me");
                        })
                    )
                    .ToList();
            }

            var responses = await Task.WhenAll(requests);
            var ids = new List<Guid>();
            foreach (var response in responses)
            {
                await Assert.That(response).HasStatus(HttpStatusCode.OK);
                ids.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            }

            await Assert.That(ids.Distinct()).IsEquivalentTo([accountId]);
        }
    }

    [Test]
    public async Task Requests_at_the_same_moment_from_someone_new_make_one_account()
    {
        for (var person = 0; person < 10; person++)
        {
            var userId = WorkOSTestIssuer.NewUserId();
            var token = WorkOS.Token(userId);

            List<Task<HttpResponseMessage>> requests;
            using (ExecutionContext.SuppressFlow())
            {
                requests = Enumerable
                    .Range(0, 6)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            using var client = Factory.ClientWithWorkOS(token);
                            return await client.GetAsync("/Me");
                        })
                    )
                    .ToList();
            }

            var responses = await Task.WhenAll(requests);

            await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.OK]);
            await Assert.That(Factory.Db.Queryable<UserAccount>().Count(a => a.WorkOSUserId == userId)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Tokens_that_are_not_for_this_app_or_not_from_workos_are_refused_and_counted_as_workos_ones()
    {
        using var metrics = new MetricsRecorder();

        async Task RefusedAsync(string token)
        {
            using var client = Factory.ClientWithWorkOS(token);
            await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        }

        await RefusedAsync(WorkOS.Token("user_expired", validFor: TimeSpan.FromMinutes(-5)));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("provider", "workos"), ("reason", "expired"))).IsEqualTo(1);

        await RefusedAsync(WorkOS.Token("user_forged", signWith: ClerkTestIssuer.AnotherKey()));
        await RefusedAsync(WorkOS.Token("user_clerk_key", signWith: Factory.Clerk.SigningKey));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("provider", "workos"), ("reason", "unknown_key"))).IsEqualTo(2);

        // Another WorkOS environment, or another app in this one
        await RefusedAsync(WorkOS.Token("user_other_client", issuer: "https://api.workos.com/user_management/client_01OTHER"));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("provider", "workos"), ("reason", "issuer"))).IsEqualTo(1);

        await RefusedAsync(WorkOS.Token("user_other_audience", audience: "client_01OTHER"));
        await Assert.That(metrics.Sum("fbs.auth.failures", ("provider", "workos"), ("reason", "audience"))).IsEqualTo(1);

        // None of them were Clerk's to refuse
        await Assert.That(metrics.Sum("fbs.auth.failures", ("provider", "clerk"))).IsEqualTo(0);
        await Assert.That(ByWorkOS("user_forged")).IsNull();
        await Assert.That(ByWorkOS("user_clerk_key")).IsNull();
    }

    [Test]
    public async Task A_token_for_this_app_by_its_audience_is_accepted()
    {
        var userId = WorkOSTestIssuer.NewUserId();
        using var client = Factory.ClientWithWorkOS(WorkOS.Token(userId, audience: WorkOSTestIssuer.ClientId));

        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Clerk_tokens_are_still_checked_by_clerk_while_both_are_on()
    {
        using var metrics = new MetricsRecorder();

        using var good = Factory.CreateClientSignedInAs(ClerkFbsApiFactory.NewUserId());
        await Assert.That(await good.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);

        using var otherApp = Factory.ClientWithWorkOS(Factory.Clerk.Token("user_other_app", authorizedParty: "https://another-app.example"));
        await Assert.That(await otherApp.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(metrics.Sum("fbs.auth.failures", ("provider", "clerk"), ("reason", "azp"))).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.auth.failures", ("provider", "workos"))).IsEqualTo(0);
    }

    [Test]
    public async Task A_good_workos_token_is_nothing_to_count_and_garbage_is_counted_once()
    {
        using var metrics = new MetricsRecorder();

        using var good = Factory.ClientWithWorkOS(WorkOS.Token(WorkOSTestIssuer.NewUserId()));
        await Assert.That(await good.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.auth.failures")).IsEqualTo(0);

        using var junk = Factory.ClientWithWorkOS("not.a.token");
        await Assert.That(await junk.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(metrics.Sum("fbs.auth.failures")).IsEqualTo(1);
    }

    [Test]
    public async Task With_only_workos_on_clerk_tokens_and_its_webhook_are_gone()
    {
        await using var factory = new WorkOSFbsApiFactory();
        using var workOS = factory.ClientWithWorkOS(factory.WorkOS.Token(WorkOSTestIssuer.NewUserId()));
        using var clerk = factory.ClientWithWorkOS(new ClerkTestIssuer().Token(ClerkFbsApiFactory.NewUserId()));
        using var anonymous = factory.CreateClient();

        await Assert.That(await workOS.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await clerk.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await anonymous.PostAsync("/webhooks/clerk", new StringContent("{}"))).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await anonymous.PostAsync("/webhooks/workos", new StringContent("{}"))).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task With_only_clerk_on_there_is_no_workos_webhook()
    {
        await using var factory = new ClerkFbsApiFactory();
        using var anonymous = factory.CreateClient();

        await Assert.That(await anonymous.PostAsync("/webhooks/workos", new StringContent("{}"))).HasStatus(HttpStatusCode.NotFound);
    }
}
