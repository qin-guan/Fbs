using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Tests;

/// <summary>Signing in with a session token from Clerk, which is checked against Clerk's keys.</summary>
public class ClerkAuthTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private HttpClient ClientWith(string? token)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }

        return client;
    }

    private UserAccount? Account(string clerkUserId) => Factory.Db.Queryable<UserAccount>().First(a => a.ClerkUserId == clerkUserId);

    [Test]
    public async Task A_session_token_from_clerk_signs_in_and_makes_an_account_from_what_it_says()
    {
        using var client = ClientWith(Factory.Clerk.Token("user_first", "first@example.com", "First Person"));

        var response = await client.GetAsync("/Me");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(me.GetProperty("name").GetString()).IsEqualTo("First Person");
        await Assert.That(me.GetProperty("email").GetString()).IsEqualTo("first@example.com");
        await Assert.That(me.GetProperty("memberships").GetArrayLength()).IsEqualTo(0);
        var account = Account("user_first")!;
        await Assert.That(account.Id).IsEqualTo(me.GetProperty("id").GetGuid());
    }

    [Test]
    public async Task The_same_person_is_the_same_account_and_what_changed_at_clerk_is_kept()
    {
        using var first = ClientWith(Factory.Clerk.Token("user_twice", "old@example.com", "Old Name"));
        var id = (await first.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid();

        using var second = ClientWith(Factory.Clerk.Token("user_twice", "new@example.com", "New Name"));
        var me = await second.GetFromJsonAsync<JsonElement>("/Me");

        await Assert.That(me.GetProperty("id").GetGuid()).IsEqualTo(id);
        await Assert.That(me.GetProperty("name").GetString()).IsEqualTo("New Name");
        await Assert.That(Factory.Db.Queryable<UserAccount>().Count(a => a.ClerkUserId == "user_twice")).IsEqualTo(1);
        await Assert.That(Account("user_twice")!.Email).IsEqualTo("new@example.com");
    }

    [Test]
    public async Task Requests_at_the_same_moment_from_someone_new_make_one_account()
    {
        // Somebody's first request, several at once, and again for a number of people, as it takes luck for
        // two of them to look for the account before either has made it
        for (var person = 0; person < 25; person++)
        {
            var userId = $"user_busy_{person}";
            var token = Factory.Clerk.Token(userId);

            List<Task<HttpResponseMessage>> requests;
            using (ExecutionContext.SuppressFlow())
            {
                requests = Enumerable
                    .Range(0, 8)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            using var client = ClientWith(token);
                            return await client.GetAsync("/Me");
                        })
                    )
                    .ToList();
            }

            var responses = await Task.WhenAll(requests);

            await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.OK]);
            await Assert.That(Factory.Db.Queryable<UserAccount>().Count(a => a.ClerkUserId == userId)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Without_a_token_or_with_one_that_is_not_a_token_it_is_unauthorized()
    {
        using var none = ClientWith(null);
        using var junk = ClientWith("not.a.token");

        await Assert.That(await none.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await junk.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_token_that_has_expired_is_unauthorized()
    {
        using var client = ClientWith(Factory.Clerk.Token("user_late", validFor: TimeSpan.FromMinutes(-5)));

        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_token_signed_with_someone_elses_key_is_unauthorized()
    {
        using var client = ClientWith(Factory.Clerk.Token("user_forged", signWith: ClerkTestIssuer.AnotherKey()));

        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(Account("user_forged")).IsNull();
    }

    [Test]
    public async Task A_token_from_another_issuer_is_unauthorized()
    {
        using var client = ClientWith(Factory.Clerk.Token("user_elsewhere", issuer: "https://someone-else.example"));

        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_token_that_was_issued_to_another_app_is_unauthorized()
    {
        using var other = ClientWith(Factory.Clerk.Token("user_other_app", authorizedParty: "https://another-app.example"));
        using var none = ClientWith(Factory.Clerk.Token("user_no_app", authorizedParty: null));

        await Assert.That(await other.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await none.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(Account("user_other_app")).IsNull();
    }

    [Test]
    public async Task A_token_with_a_different_algorithm_is_unauthorized()
    {
        // Signed with the same key, but with another algorithm than the one that is accepted
        using var client = ClientWith(Factory.Clerk.Token("user_hs", signWith: new SymmetricSecurityKey(new byte[64]), algorithm: SecurityAlgorithms.HmacSha256));

        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task An_account_that_was_deleted_is_unauthorized_even_with_a_token_that_has_not_expired()
    {
        using var client = ClientWith(Factory.Clerk.Token("user_gone"));
        (await client.GetAsync("/Me")).EnsureSuccessStatusCode();
        Factory.Db.Updateable<UserAccount>().SetColumns(a => new UserAccount { DeletedAt = DateTimeOffset.UtcNow }).Where(a => a.ClerkUserId == "user_gone").ExecuteCommand();

        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task What_the_account_belongs_to_is_listed_and_what_it_has_left_is_not()
    {
        using var client = ClientWith(Factory.Clerk.Token("user_member", name: "A Member"));
        var accountId = (await client.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid();
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { UserId = accountId, Status = MemberStatus.Active, Role = MemberRole.Admin }).Where(m => m.Id == Factory.MemberIdOf(Users.Admin)).ExecuteCommand();
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { UserId = accountId, Status = MemberStatus.Removed }).Where(m => m.Id == Factory.MemberIdOf(Users.Booker)).ExecuteCommand();

        var me = await client.GetFromJsonAsync<JsonElement>("/Me");

        var membership = me.GetProperty("memberships").EnumerateArray().Single();
        await Assert.That(membership.GetProperty("tenantSlug").GetString()).IsEqualTo(Factory.Slug);
        await Assert.That(membership.GetProperty("displayName").GetString()).IsEqualTo("MAJ Admin");
        await Assert.That(membership.GetProperty("status").GetString()).IsEqualTo("Active");
    }

    [Test]
    public async Task The_phone_number_and_cookie_sign_in_is_unaffected()
    {
        using var client = Factory.CreateClientFor(Users.Booker);

        (await client.GetAsync("/Booking")).EnsureSuccessStatusCode();
        // And a token from Clerk is not what those endpoints ask for
        using var clerk = ClientWith(Factory.Clerk.Token("user_legacy"));
        await Assert.That(await clerk.GetAsync("/Booking")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Without_clerk_turned_on_there_is_nothing_for_it()
    {
        await using var factory = new FbsApiFactory();
        using var client = factory.CreateClient();

        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Clerk_without_the_database_refuses_to_start_and_says_why()
    {
        await using var factory = new ClerkWithoutDatabaseFactory();

        var exception = await Assert.That(() => factory.CreateClient()).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("Storage:Provider=Database");
    }

    [Test]
    public async Task Clerk_without_authorized_parties_refuses_to_start_and_says_why()
    {
        await using var factory = new ClerkWithoutPartiesFactory();

        var exception = await Assert.That(() => factory.CreateClient()).Throws<Exception>();

        await Assert.That(exception!.ToString()).Contains("Clerk:AuthorizedParties");
    }

    private sealed class ClerkWithoutDatabaseFactory : FbsApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Clerk:Issuer", ClerkTestIssuer.Issuer);
            builder.UseSetting("Clerk:AuthorizedParties:0", ClerkTestIssuer.App);
        }
    }

    private sealed class ClerkWithoutPartiesFactory : DatabaseFbsApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Clerk:Issuer", ClerkTestIssuer.Issuer);
        }
    }
}
