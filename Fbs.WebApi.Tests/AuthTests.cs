using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using TUnit.Assertions.Enums;

namespace Fbs.WebApi.Tests;

public class AuthTests
{
    [ClassDataSource<FbsApiFactory>]
    public required FbsApiFactory Factory { get; init; }

    private HttpClient _client = null!;

    [Before(Test)]
    public void CreateClient()
    {
        _client = Factory.CreateClient();
    }

    private async Task<string> RequestCodeAsync(string phone)
    {
        var sent = Factory.Telegram.Messages.Count;
        (await _client.PostAsJsonAsync("/Auth/Login", new { phone })).EnsureSuccessStatusCode();

        var message = (await Factory.Telegram.WaitForMessagesAsync(sent + 1))[sent];
        return Regex.Match(message.Text, @"\d{6}").Value;
    }

    private Task<HttpResponseMessage> VerifyAsync(string phone, string code) =>
        _client.PostAsJsonAsync("/Auth/Verify", new { phone, code });

    /// <summary>The requests made to Google Sheets while running the action.</summary>
    private async Task<List<string>> SheetRequestsDuringAsync(Func<Task> action)
    {
        var before = Factory.Google.Requests.Count;
        await action();
        return Factory
            .Google.Requests.Skip(before)
            .Where(r => r.Path.StartsWith("v4/"))
            .Select(r => $"{r.Method} {r.Path}")
            .ToList();
    }

    [Test]
    public async Task Signs_in_with_the_code_sent_on_telegram()
    {
        var code = await RequestCodeAsync(Users.Booker);

        var response = await VerifyAsync(Users.Booker, code);

        response.EnsureSuccessStatusCode();
        await Assert
            .That(response.Headers.GetValues("Set-Cookie"))
            .Contains(c => c.StartsWith(".AspNetCore.Cookies="));
        // The code can't be used again
        var header = await Assert.That(Factory.Google.Sheets["OTPs"]).HasSingleItem();
        await Assert.That(header).IsEquivalentTo(["Phone", "Code", "CreatedAt"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_wrong_code_is_rejected()
    {
        var code = await RequestCodeAsync(Users.Booker);
        var wrong = code == "000000" ? "111111" : "000000";

        var response = await VerifyAsync(Users.Booker, wrong);

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(Factory.Google.Sheets["OTPs"]).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Signing_in_does_not_repeat_calls_to_google()
    {
        // Also loads the users and looks up the sheet's ID, which are remembered from then on
        (await VerifyAsync(Users.SameUnit, await RequestCodeAsync(Users.SameUnit))).EnsureSuccessStatusCode();

        var code = "";
        var login = await SheetRequestsDuringAsync(async () => code = await RequestCodeAsync(Users.Booker));
        var verify = await SheetRequestsDuringAsync(async () =>
            (await VerifyAsync(Users.Booker, code)).EnsureSuccessStatusCode()
        );

        // Reads the codes to check one wasn't sent too recently, then adds one without reading it back
        await Assert
            .That(login)
            .IsEquivalentTo(
                ["GET v4/spreadsheets/spreadsheet/values/OTPs", "POST v4/spreadsheets/spreadsheet/values/OTPs:append"],
                CollectionOrdering.Matching
            );
        // Reads the code to check it, then again to find its row before deleting it
        await Assert
            .That(verify)
            .IsEquivalentTo(
                [
                    "GET v4/spreadsheets/spreadsheet/values/OTPs",
                    "GET v4/spreadsheets/spreadsheet/values/OTPs",
                    "POST v4/spreadsheets/spreadsheet:batchUpdate",
                ],
                CollectionOrdering.Matching
            );
    }
}
