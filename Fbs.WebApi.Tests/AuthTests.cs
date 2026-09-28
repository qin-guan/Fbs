using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Fbs.WebApi.Tests;

public class AuthTests : IDisposable
{
    private readonly FbsApiFactory _factory = new();
    private readonly HttpClient _client;

    public AuthTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> RequestCodeAsync(string phone)
    {
        var sent = _factory.Telegram.Messages.Count;
        (await _client.PostAsJsonAsync("/Auth/Login", new { phone })).EnsureSuccessStatusCode();

        var message = (await _factory.Telegram.WaitForMessagesAsync(sent + 1))[sent];
        return Regex.Match(message.Text, @"\d{6}").Value;
    }

    private Task<HttpResponseMessage> VerifyAsync(string phone, string code) =>
        _client.PostAsJsonAsync("/Auth/Verify", new { phone, code });

    /// <summary>The requests made to Google Sheets while running the action.</summary>
    private async Task<List<string>> SheetRequestsDuringAsync(Func<Task> action)
    {
        var before = _factory.Google.Requests.Count;
        await action();
        return _factory
            .Google.Requests.Skip(before)
            .Where(r => r.Path.StartsWith("v4/"))
            .Select(r => $"{r.Method} {r.Path}")
            .ToList();
    }

    [Fact]
    public async Task Signs_in_with_the_code_sent_on_telegram()
    {
        var code = await RequestCodeAsync(Users.Booker);

        var response = await VerifyAsync(Users.Booker, code);

        response.EnsureSuccessStatusCode();
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Cookies="));
        // The code can't be used again
        Assert.Equal([["Phone", "Code", "CreatedAt"]], _factory.Google.Sheets["OTPs"]);
    }

    [Fact]
    public async Task A_wrong_code_is_rejected()
    {
        var code = await RequestCodeAsync(Users.Booker);
        var wrong = code == "000000" ? "111111" : "000000";

        var response = await VerifyAsync(Users.Booker, wrong);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, _factory.Google.Sheets["OTPs"].Count);
    }

    [Fact]
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
        Assert.Equal(
            ["GET v4/spreadsheets/spreadsheet/values/OTPs", "POST v4/spreadsheets/spreadsheet/values/OTPs:append"],
            login
        );
        // Reads the code to check it, then again to find its row before deleting it
        Assert.Equal(
            [
                "GET v4/spreadsheets/spreadsheet/values/OTPs",
                "GET v4/spreadsheets/spreadsheet/values/OTPs",
                "POST v4/spreadsheets/spreadsheet:batchUpdate",
            ],
            verify
        );
    }
}
