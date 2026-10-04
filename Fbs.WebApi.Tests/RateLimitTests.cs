using System.Net;
using System.Net.Http.Json;
using System.Text;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Fbs.WebApi.Tests;

/// <summary>How often each thing can be done, and whose address is believed when the API is behind a proxy.</summary>
public class RateLimitTests
{
    [ClassDataSource<LimitedFactory>]
    public required LimitedFactory Factory { get; init; }

    /// <summary>Three of each, in a minute.</summary>
    public class LimitedFactory : ClerkFbsApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            foreach (var policy in new[] { "create-organization", "join", "link-telegram", "webhook" })
            {
                builder.UseSetting($"RateLimits:Limits:{policy}:PermitLimit", "3");
                builder.UseSetting($"RateLimits:Limits:{policy}:WindowSeconds", "60");
            }

            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, RemoteAddressFilter>());
        }
    }

    /// <summary>Stands in for the connection the request came over, which a test server doesn't have, from a header.</summary>
    private sealed class RemoteAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    async (context, nextMiddleware) =>
                    {
                        if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var address))
                        {
                            context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                        }

                        await nextMiddleware();
                    }
                );
                next(app);
            };
    }

    private static Task<HttpResponseMessage> TryToMakeAnOrganizationAsync(HttpClient client) => client.PostAsJsonAsync("/Tenants", new { name = "x", slug = "!" });

    private async Task<HttpResponseMessage> WebhookAsync(string remote, string? forwardedFor)
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/clerk") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Test-Remote-Ip", remote);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request);
    }

    [Test]
    public async Task Somebody_who_asks_too_often_is_told_to_wait_and_others_are_not_affected()
    {
        var busy = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        var other = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        for (var i = 0; i < 3; i++)
        {
            await Assert.That(await TryToMakeAnOrganizationAsync(busy)).HasStatus(HttpStatusCode.BadRequest);
        }

        var limited = await TryToMakeAnOrganizationAsync(busy);

        await Assert.That(limited).HasStatus(HttpStatusCode.TooManyRequests);
        await Assert.That(limited.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0).IsGreaterThan(0);
        await Assert.That(await limited.Content.ReadAsStringAsync()).Contains("rate-limited");
        await Assert.That(await TryToMakeAnOrganizationAsync(other)).HasStatus(HttpStatusCode.BadRequest);
        // What isn't limited is not, however much they have done
        await Assert.That(await busy.GetAsync("/Me")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Looking_at_and_using_links_to_join_and_to_claim_share_a_limit_for_each_person()
    {
        var person = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());
        var other = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        await Assert.That(await person.GetAsync($"/Invites/{new string('a', 43)}")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await person.PostAsJsonAsync($"/Invites/{new string('a', 43)}/Accept", new { })).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await person.GetAsync("/Claims/no-such-organization")).HasStatus(HttpStatusCode.NotFound);

        await Assert.That(await person.PostAsync("/Claims/no-such-organization/Start", null)).HasStatus(HttpStatusCode.TooManyRequests);
        await Assert.That(await person.GetAsync($"/Invites/{new string('b', 43)}")).HasStatus(HttpStatusCode.TooManyRequests);
        await Assert.That(await other.GetAsync($"/Invites/{new string('b', 43)}")).HasStatus(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Making_links_for_telegram_is_limited_for_each_person()
    {
        var person = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        for (var i = 0; i < 3; i++)
        {
            await Assert.That(await person.PostAsync("/Me/Telegram/Link", null)).HasStatus(HttpStatusCode.OK);
        }

        await Assert.That(await person.PostAsync("/Me/Telegram/Link", null)).HasStatus(HttpStatusCode.TooManyRequests);
        await Assert.That(await person.GetAsync("/Me/Telegram")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Behind_a_proxy_in_a_private_range_each_client_it_forwards_is_counted_on_its_own()
    {
        for (var i = 0; i < 3; i++)
        {
            await Assert.That(await WebhookAsync("10.0.0.5", "203.0.113.9")).HasStatus(HttpStatusCode.Unauthorized);
        }

        await Assert.That(await WebhookAsync("10.0.0.5", "203.0.113.9")).HasStatus(HttpStatusCode.TooManyRequests);
        // Another client through the same proxy, who is not affected by the first, and the proxy itself, who has not been counted
        await Assert.That(await WebhookAsync("10.0.0.5", "203.0.113.10")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await WebhookAsync("10.0.0.5", null)).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await WebhookAsync("172.20.1.1", "203.0.113.9")).HasStatus(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Somebody_who_reaches_the_api_directly_cannot_say_who_they_are_with_a_header()
    {
        // Each says they are somebody else, and they are all the same address
        for (var i = 0; i < 3; i++)
        {
            await Assert.That(await WebhookAsync("198.51.100.7", $"203.0.113.{i + 20}")).HasStatus(HttpStatusCode.Unauthorized);
        }

        await Assert.That(await WebhookAsync("198.51.100.7", "203.0.113.99")).HasStatus(HttpStatusCode.TooManyRequests);
        await Assert.That(await WebhookAsync("198.51.100.8", "203.0.113.99")).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Nothing_is_limited_when_limits_are_turned_off()
    {
        await using var factory = new UnlimitedFactory();
        var person = factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        for (var i = 0; i < 15; i++)
        {
            await Assert.That(await TryToMakeAnOrganizationAsync(person)).HasStatus(HttpStatusCode.BadRequest);
        }
    }

    private sealed class UnlimitedFactory : LimitedFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimits:Enabled", "false");
        }
    }

    [Test]
    public async Task The_limits_that_are_set_without_being_configured_are_ones_that_normal_use_does_not_reach()
    {
        await using var factory = new ClerkFbsApiFactory();
        var person = factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        // Ten organisations an hour, and a lot of looking at links, which is far more than anybody needs
        for (var i = 0; i < 10; i++)
        {
            await Assert.That(await TryToMakeAnOrganizationAsync(person)).HasStatus(HttpStatusCode.BadRequest);
        }

        await Assert.That(await TryToMakeAnOrganizationAsync(person)).HasStatus(HttpStatusCode.TooManyRequests);
        for (var i = 0; i < 25; i++)
        {
            await Assert.That(await person.GetAsync("/Claims/none")).HasStatus(HttpStatusCode.NotFound);
        }
    }
}
