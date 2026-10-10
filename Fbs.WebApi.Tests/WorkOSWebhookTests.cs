using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;

namespace Fbs.WebApi.Tests;

/// <summary>
/// What happens to an account when WorkOS says it was deleted, and when Clerk says so about somebody who has moved to WorkOS, which is
/// nothing: emptying Clerk after the move must not erase anybody.
/// </summary>
public class WorkOSWebhookTests
{
    [ClassDataSource<MovingFbsApiFactory>]
    public required MovingFbsApiFactory Factory { get; init; }

    private static string Deleted(string workOSUserId) =>
        $$"""{"id":"event_01TEST","event":"user.deleted","data":{"object":"user","id":"{{workOSUserId}}","email":"someone@example.com","email_verified":true},"created_at":"2026-10-10T00:00:00.000Z"}""";

    private async Task<HttpResponseMessage> PostAsync(string body, Dictionary<string, string>? headers = null, string path = "/webhooks/workos", DatabaseFbsApiFactory? factory = null)
    {
        using var client = (factory ?? Factory).CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers ?? WorkOSTestIssuer.WebhookHeaders(body))
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request);
    }

    private UserAccount Account(Guid id) => Factory.Db.Queryable<UserAccount>().First(a => a.Id == id);

    /// <summary>Somebody who had a Clerk account, in an organisation, who has signed in with WorkOS since.</summary>
    private async Task<(TestOrg Org, Guid AccountId, string ClerkUserId, string WorkOSUserId, HttpClient Client)> MovedAsync()
    {
        var org = await Factory.CreateOrgAsync();
        var clerkUserId = ClerkFbsApiFactory.NewUserId();
        var clerk = Factory.ClientFor(clerkUserId, "Mover");
        var accountId = await Factory.AccountIdOfAsync(clerk);
        Factory.Db.Insertable(new TenantMember { Id = Guid.NewGuid(), TenantId = org.TenantId, UserId = accountId, DisplayName = "Mover", Status = MemberStatus.Active }).ExecuteCommand();
        var workOSUserId = WorkOSTestIssuer.NewUserId();
        var client = Factory.ClientWithWorkOS(Factory.WorkOS.Token(workOSUserId, clerkUserId: clerkUserId));
        (await client.GetAsync("/Me")).EnsureSuccessStatusCode();
        return (org, accountId, clerkUserId, workOSUserId, client);
    }

    [Test]
    public async Task A_user_deleted_in_workos_is_erased_and_what_they_made_stays_without_them_in_it()
    {
        var (org, accountId, _, workOSUserId, client) = await MovedAsync();
        using var metrics = new MetricsRecorder();

        var response = await PostAsync(Deleted(workOSUserId));

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var account = Account(accountId);
        await Assert.That(account.DeletedAt).IsNotNull();
        await Assert.That(account.Name).IsNull();
        await Assert.That(account.Email).IsNull();
        var place = Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId && m.UserId == accountId);
        await Assert.That(place.DisplayName).IsEqualTo("Former member");
        await Assert.That(place.Status).IsEqualTo(MemberStatus.Removed);
        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(metrics.Sum("fbs.accounts.erased")).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.webhooks.received", ("provider", "workos"), ("type", "user.deleted"), ("result", "accepted"))).IsEqualTo(1);
        client.Dispose();
    }

    [Test]
    public async Task A_user_deleted_in_clerk_after_they_moved_to_workos_is_left_as_they_are()
    {
        var (org, accountId, clerkUserId, _, client) = await MovedAsync();
        var body = $$"""{"type":"user.deleted","data":{"deleted":true,"id":"{{clerkUserId}}","object":"user"},"object":"event"}""";

        var response = await PostAsync(body, ClerkTestIssuer.WebhookHeaders($"msg_{Guid.NewGuid():N}", body), "/webhooks/clerk");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        await Assert.That(Account(accountId).DeletedAt).IsNull();
        await Assert.That(Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId && m.UserId == accountId).Status).IsEqualTo(MemberStatus.Active);
        await Assert.That(await client.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
        client.Dispose();
    }

    [Test]
    public async Task A_user_deleted_in_clerk_who_has_not_moved_is_still_erased()
    {
        var clerkUserId = ClerkFbsApiFactory.NewUserId();
        var accountId = await Factory.AccountIdOfAsync(Factory.ClientFor(clerkUserId));
        var body = $$"""{"type":"user.deleted","data":{"deleted":true,"id":"{{clerkUserId}}","object":"user"},"object":"event"}""";

        await Assert.That(await PostAsync(body, ClerkTestIssuer.WebhookHeaders($"msg_{Guid.NewGuid():N}", body), "/webhooks/clerk")).HasStatus(HttpStatusCode.OK);

        await Assert.That(Account(accountId).DeletedAt).IsNotNull();
    }

    [Test]
    public async Task Being_told_twice_or_about_somebody_never_seen_or_of_something_else_changes_nothing()
    {
        var workOSUserId = WorkOSTestIssuer.NewUserId();
        using var person = Factory.ClientWithWorkOS(Factory.WorkOS.Token(workOSUserId));
        var accountId = (await person.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid();
        var body = Deleted(workOSUserId);
        var headers = WorkOSTestIssuer.WebhookHeaders(body);

        await Assert.That(await PostAsync(body, headers)).HasStatus(HttpStatusCode.OK);
        var erasedAt = Account(accountId).DeletedAt;
        await Task.Delay(50);
        await Assert.That(await PostAsync(body, headers)).HasStatus(HttpStatusCode.OK);
        await Assert.That(Account(accountId).DeletedAt).IsEqualTo(erasedAt);

        var kept = WorkOSTestIssuer.NewUserId();
        using var keeper = Factory.ClientWithWorkOS(Factory.WorkOS.Token(kept));
        var keptId = (await keeper.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid();
        await Assert.That(await PostAsync(Deleted("user_01NEVERSEEN"))).HasStatus(HttpStatusCode.OK);
        await Assert.That(await PostAsync(Deleted(kept).Replace("user.deleted", "user.updated"))).HasStatus(HttpStatusCode.OK);
        await Assert.That(await PostAsync("this is not json at all")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await PostAsync("""{"event":"user.deleted"}""")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await PostAsync("""{"event":"user.deleted","data":{"id":42}}""")).HasStatus(HttpStatusCode.OK);
        await Assert.That(Account(keptId).DeletedAt).IsNull();
        await Assert.That(await keeper.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_webhook_that_is_not_signed_by_workos_is_refused_and_does_nothing()
    {
        var workOSUserId = WorkOSTestIssuer.NewUserId();
        using var person = Factory.ClientWithWorkOS(Factory.WorkOS.Token(workOSUserId));
        var accountId = (await person.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid();
        var body = Deleted(workOSUserId);
        using var metrics = new MetricsRecorder();

        var responses = new[]
        {
            await PostAsync(body, []),
            await PostAsync(body, WorkOSTestIssuer.WebhookHeaders(body, secret: "somebody-elses-secret")),
            await PostAsync(body, WorkOSTestIssuer.WebhookHeaders(body, sentAt: DateTimeOffset.UtcNow.AddHours(-1))),
            // Signed for something else
            await PostAsync(body, WorkOSTestIssuer.WebhookHeaders(Deleted("user_01OTHER"))),
            // Signed the way Clerk does
            await PostAsync(body, ClerkTestIssuer.WebhookHeaders("msg_1", body)),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.Unauthorized]);
        await Assert.That(Account(accountId).DeletedAt).IsNull();
        await Assert.That(await person.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
        await Assert.That(metrics.Sum("fbs.webhooks.received", ("provider", "workos"), ("type", "unknown"), ("result", "invalid_signature"))).IsEqualTo(5);
    }

    [Test]
    public async Task A_body_that_is_too_large_is_refused()
    {
        var body = new string(' ', 300 * 1024);

        await Assert.That((await PostAsync(body)).StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
    }

    [Test]
    public async Task Without_a_secret_set_webhooks_are_not_accepted()
    {
        await using var noSecret = new WorkOSWithoutSecretFactory();

        await Assert.That(await PostAsync(Deleted("user_01X"), factory: noSecret)).HasStatus(HttpStatusCode.ServiceUnavailable);
    }

    private sealed class WorkOSWithoutSecretFactory : WorkOSFbsApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("WorkOS:WebhookSecret", "");
        }
    }
}
