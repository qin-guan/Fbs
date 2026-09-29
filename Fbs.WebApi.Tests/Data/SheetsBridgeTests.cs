using System.Net;
using System.Net.Http.Json;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;

namespace Fbs.WebApi.Tests.Data;

/// <summary>The API with the sheets kept in step with the database, as it runs until there are screens to manage people in.</summary>
public sealed class SheetsBridgeFbsApiFactory : DatabaseFbsApiFactory
{
    public SheetsBridgeFbsApiFactory()
    {
        Google.Sheets["Nominal Roll"] = [["Name", "Unit", "Phone"]];
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ReferenceData:Sheets:Enabled", "true");
        builder.UseSetting("ReferenceData:Sheets:InitialDelay", "00:00:00");
        builder.UseSetting("ReferenceData:Sheets:Interval", "00:00:01");
    }
}

[ClassDataSource<SheetsBridgeFbsApiFactory>]
public class SheetsBridgeTests(SheetsBridgeFbsApiFactory factory)
{
    private async Task<HttpStatusCode> LoginAsync(string phone)
    {
        using var client = factory.CreateClient();
        return (await client.PostAsJsonAsync("/Auth/Login", new { phone })).StatusCode;
    }

    private async Task<bool> BecomesAsync(string phone, HttpStatusCode expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await LoginAsync(phone) == expected)
            {
                return true;
            }

            await Task.Delay(300);
        }

        return false;
    }

    [Test]
    public async Task Someone_added_to_the_users_sheet_can_sign_in_once_it_has_been_looked_at()
    {
        await Assert.That(await LoginAsync("6590003333")).IsEqualTo(HttpStatusCode.BadRequest);

        factory.Google.Sheets["Users"].Add(["Alpha", "PTE Newcomer", "6590003333", "5555", "None", "FALSE"]);

        await Assert.That(await BecomesAsync("6590003333", HttpStatusCode.NoContent)).IsTrue();
        var messages = await factory.Telegram.WaitForMessagesAsync(1);
        await Assert.That(messages.Select(m => m.ChatId)).Contains(5555L);
    }

    [Test]
    public async Task Someone_taken_off_the_users_sheet_can_no_longer_sign_in()
    {
        // The others are on the sheet, so this is one of six and not a mistake with it
        factory.AddUser("Alpha", "PTE Extra", "6590004444", "4444", "None");
        factory.Google.Sheets["Users"].Add(["Alpha", "PTE Extra", "6590004444", "4444", "None", "FALSE"]);
        await Assert.That(await BecomesAsync("6590004444", HttpStatusCode.NoContent)).IsTrue();
        factory.Google.Sheets["Users"].RemoveAll(row => row[2] == "6590004444");

        await Assert.That(await BecomesAsync("6590004444", HttpStatusCode.BadRequest)).IsTrue();
    }
}
