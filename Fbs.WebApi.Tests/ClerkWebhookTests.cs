using System.Net;
using System.Net.Http.Json;
using System.Text;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;

namespace Fbs.WebApi.Tests;

/// <summary>What happens to an account when Clerk says it was deleted.</summary>
public class ClerkWebhookTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static string Deleted(string clerkUserId) => $$"""{"type":"user.deleted","data":{"deleted":true,"id":"{{clerkUserId}}","object":"user"},"object":"event"}""";

    private async Task<HttpResponseMessage> PostAsync(string body, Dictionary<string, string>? headers = null, ClerkFbsApiFactory? factory = null)
    {
        using var client = (factory ?? Factory).CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/clerk") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers ?? ClerkTestIssuer.WebhookHeaders($"msg_{Guid.NewGuid():N}", body))
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request);
    }

    private UserAccount Account(string clerkUserId) => Factory.Db.Queryable<UserAccount>().First(a => a.ClerkUserId == clerkUserId);

    [Test]
    public async Task A_deleted_account_is_erased_and_what_they_made_stays_without_them_in_it()
    {
        var org = await Factory.CreateOrgAsync();
        var other = await Factory.CreateOrgAsync();
        var userId = ClerkFbsApiFactory.NewUserId();
        var leaver = Factory.ClientFor(userId, "Leaver");
        var accountId = await Factory.AccountIdOfAsync(leaver);
        var placeId = Guid.NewGuid();
        Factory.Db.Insertable(new TenantMember { Id = placeId, TenantId = org.TenantId, UserId = accountId, DisplayName = "Leaver", Phone = "+6591234567", LegacyChatId = "4242", Role = MemberRole.Admin, NotificationScope = NotificationScope.All, Status = MemberStatus.Active }).ExecuteCommand();
        var otherPlace = Guid.NewGuid();
        Factory.Db.Insertable(new TenantMember { Id = otherPlace, TenantId = other.TenantId, UserId = accountId, DisplayName = "Leaver", Status = MemberStatus.Pending }).ExecuteCommand();
        Factory.Db.Insertable(new TelegramLink { Id = Guid.NewGuid(), UserId = accountId, ChatId = "777001", LinkedAt = DateTimeOffset.UtcNow }).ExecuteCommand();
        Factory.Db.Insertable(new MemberClaimToken { Id = Guid.NewGuid(), UserId = accountId, TenantId = org.TenantId, TokenHash = new string('a', 64), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) }).ExecuteCommand();
        var facility = org.AddFacility("Hall");
        var bookingId = Guid.NewGuid();
        Factory.Db.Insertable(new DataBooking { Id = bookingId, TenantId = org.TenantId, FacilityId = facility, StartUtc = DateTimeOffset.UtcNow.AddDays(1), EndUtc = DateTimeOffset.UtcNow.AddDays(1).AddHours(1), Conduct = "Lesson", BookedByMemberId = placeId }).ExecuteCommand();
        var bystander = Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), "Bystander");
        var bystanderAccount = await Factory.AccountIdOfAsync(bystander);

        var response = await PostAsync(Deleted(userId));

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var account = Account(userId);
        await Assert.That(account.DeletedAt).IsNotNull();
        await Assert.That(account.Name).IsNull();
        await Assert.That(account.Email).IsNull();
        var place = Factory.Db.Queryable<TenantMember>().First(m => m.Id == placeId);
        await Assert.That(place.DisplayName).IsEqualTo("Former member");
        await Assert.That(place.Phone).IsNull();
        await Assert.That(place.LegacyChatId).IsNull();
        await Assert.That(place.Role).IsEqualTo(MemberRole.Member);
        await Assert.That(place.NotificationScope).IsEqualTo(NotificationScope.None);
        await Assert.That(place.Status).IsEqualTo(MemberStatus.Removed);
        await Assert.That(Factory.Db.Queryable<TenantMember>().First(m => m.Id == otherPlace).Status).IsEqualTo(MemberStatus.Removed);
        await Assert.That(Factory.Db.Queryable<TelegramLink>().Any(l => l.UserId == accountId)).IsFalse();
        await Assert.That(Factory.Db.Queryable<MemberClaimToken>().Any(t => t.UserId == accountId)).IsFalse();
        // The booking is still theirs, as a former member
        var listed = await org.Admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/t/{org.Slug}/Bookings?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(3).ToString("O"))}");
        await Assert.That(listed.EnumerateArray().Single().GetProperty("bookedBy").GetProperty("displayName").GetString()).IsEqualTo("Former member");
        await Assert.That(Factory.Db.Queryable<DataBooking>().First(b => b.Id == bookingId).BookedByMemberId).IsEqualTo(placeId);
        // Their session is no good, and somebody else's is untouched
        await Assert.That(await leaver.GetAsync("/Me")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await bystander.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
        await Assert.That(Account(Factory.Db.Queryable<UserAccount>().First(a => a.Id == bystanderAccount).ClerkUserId).DeletedAt).IsNull();
    }

    [Test]
    public async Task Being_told_twice_or_about_somebody_who_never_signed_in_or_of_something_else_changes_nothing()
    {
        var userId = ClerkFbsApiFactory.NewUserId();
        var person = Factory.ClientFor(userId, "Person");
        await Factory.AccountIdOfAsync(person);
        var body = Deleted(userId);
        var headers = ClerkTestIssuer.WebhookHeaders("msg_same", body);

        await Assert.That(await PostAsync(body, headers)).HasStatus(HttpStatusCode.OK);
        var erasedAt = Account(userId).DeletedAt;
        await Task.Delay(50);
        await Assert.That(await PostAsync(body, headers)).HasStatus(HttpStatusCode.OK);
        await Assert.That(Account(userId).DeletedAt).IsEqualTo(erasedAt);

        await Assert.That(await PostAsync(Deleted("user_never_seen"))).HasStatus(HttpStatusCode.OK);
        var other = ClerkFbsApiFactory.NewUserId();
        var kept = Factory.ClientFor(other, "Kept");
        await Factory.AccountIdOfAsync(kept);
        await Assert.That(await PostAsync("{\"type\":\"user.updated\",\"data\":{\"id\":\"" + other + "\"}}")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await PostAsync("this is not json at all")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await PostAsync("""{"type":"user.deleted"}""")).HasStatus(HttpStatusCode.OK);
        await Assert.That(Account(other).DeletedAt).IsNull();
        await Assert.That(await kept.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_webhook_that_is_not_signed_by_clerk_is_refused_and_does_nothing()
    {
        var userId = ClerkFbsApiFactory.NewUserId();
        var person = Factory.ClientFor(userId, "Person");
        await Factory.AccountIdOfAsync(person);
        var body = Deleted(userId);
        var other = "whsec_" + Convert.ToBase64String(new byte[32]);

        var responses = new[]
        {
            await PostAsync(body, []),
            await PostAsync(body, ClerkTestIssuer.WebhookHeaders("msg_1", body, secret: other)),
            await PostAsync(body, ClerkTestIssuer.WebhookHeaders("msg_1", body, sentAt: DateTimeOffset.UtcNow.AddHours(-1))),
            // Signed for something else
            await PostAsync(body, ClerkTestIssuer.WebhookHeaders("msg_1", Deleted("user_other"))),
        };

        await Assert.That(responses.Select(r => r.StatusCode).Distinct()).IsEquivalentTo([HttpStatusCode.Unauthorized]);
        await Assert.That(Account(userId).DeletedAt).IsNull();
        await Assert.That(await person.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_body_that_is_too_large_is_refused()
    {
        var body = new string(' ', 300 * 1024);

        var response = await PostAsync(body);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
    }

    [Test]
    public async Task Without_a_secret_set_webhooks_are_not_accepted_and_without_clerk_there_is_no_endpoint()
    {
        await using var noSecret = new ClerkWithoutSecretFactory();
        var body = Deleted("user_x");

        await Assert.That(await PostAsync(body, factory: noSecret)).HasStatus(HttpStatusCode.ServiceUnavailable);

        await using var withoutClerk = new FbsApiFactory();
        using var client = withoutClerk.CreateClient();
        await Assert.That(await client.PostAsync("/webhooks/clerk", new StringContent(body))).HasStatus(HttpStatusCode.NotFound);
    }

    private sealed class ClerkWithoutSecretFactory : ClerkFbsApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Clerk:WebhookSecret", "");
        }
    }
}
