using System.Net;
using System.Net.Http.Json;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;

namespace Fbs.WebApi.Tests;

/// <summary>The API while the data is being moved: everything can be read, and nothing can be changed.</summary>
public sealed class ReadOnlyFbsApiFactory : FbsApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Maintenance:ReadOnly", "true");
    }
}

public class MaintenanceTests
{
    [ClassDataSource<ReadOnlyFbsApiFactory>]
    public required ReadOnlyFbsApiFactory Factory { get; init; }

    [Test]
    public async Task Things_can_still_be_read()
    {
        using var client = Factory.CreateClientFor(Users.Booker);

        (await client.GetAsync("/Booking")).EnsureSuccessStatusCode();
        (await client.GetAsync("/Facility")).EnsureSuccessStatusCode();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
    }

    [Test]
    public async Task Nothing_can_be_changed_and_it_says_to_try_again()
    {
        using var client = Factory.CreateClientFor(Users.Booker);

        var made = await client.PostAsJsonAsync(
            "/Booking",
            new
            {
                conduct = "Range",
                facilityName = "Field",
                startDateTime = DateTimeOffset.UtcNow.AddDays(3).Date.AddHours(8),
                endDateTime = DateTimeOffset.UtcNow.AddDays(3).Date.AddHours(10),
            }
        );
        var cancelled = await client.DeleteAsync($"/Booking/{Guid.NewGuid()}");
        var login = await client.PostAsJsonAsync("/Auth/Login", new { phone = Users.Booker });

        foreach (var response in new[] { made, cancelled, login })
        {
            await Assert.That(response).HasStatus(HttpStatusCode.ServiceUnavailable);
            await Assert.That(response.Headers.RetryAfter).IsNotNull();
            await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        }

        // Nothing was made, and Telegram was not told
        await Assert.That(Factory.Google.Events(Fakes.FakeGoogle.MainCalendar)).IsEmpty();
        await Task.Delay(200);
        await Assert.That(Factory.Telegram.Messages).IsEmpty();
    }

    [Test]
    public async Task A_telegram_update_is_refused_so_that_telegram_sends_it_again_later()
    {
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", FbsApiFactory.TelegramWebhookSecret);

        var response = await client.PostAsJsonAsync("/Bot", new { update_id = 1 });

        await Assert.That(response).HasStatus(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task Without_the_setting_nothing_is_refused()
    {
        await using var normal = new FbsApiFactory();
        using var client = normal.CreateClientFor(Users.Booker);

        var response = await client.DeleteAsync($"/Booking/{Guid.NewGuid()}");

        await Assert.That(response).HasStatus(HttpStatusCode.NotFound);
    }
}
