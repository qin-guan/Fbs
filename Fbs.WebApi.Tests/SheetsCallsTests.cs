using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Fbs.WebApi.Tests.Data;
using TUnit.Assertions.Enums;

namespace Fbs.WebApi.Tests;

/// <summary>
/// How often signing in goes to Google Sheets. Goes away with the Sheets storage.
/// </summary>
public class SheetsCallsTests
{
    [ClassDataSource<FbsApiFactory>]
    public required FbsApiFactory Factory { get; init; }

    private async Task<string> RequestCodeAsync(HttpClient client, string phone)
    {
        var sent = Factory.Telegram.Messages.Count;
        (await client.PostAsJsonAsync("/Auth/Login", new { phone })).EnsureSuccessStatusCode();

        var message = (await Factory.Telegram.WaitForMessagesAsync(sent + 1))[sent];
        return Regex.Match(message.Text, @"\d{6}").Value;
    }

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
    public async Task Signing_in_does_not_repeat_calls_to_google()
    {
        using var client = Factory.CreateClient();
        // Also loads the users and looks up the sheet's ID, which are remembered from then on
        var first = await RequestCodeAsync(client, Users.SameUnit);
        (await client.PostAsJsonAsync("/Auth/Verify", new { phone = Users.SameUnit, code = first })).EnsureSuccessStatusCode();

        var code = "";
        var login = await SheetRequestsDuringAsync(async () => code = await RequestCodeAsync(client, Users.Booker));
        var verify = await SheetRequestsDuringAsync(async () =>
            (await client.PostAsJsonAsync("/Auth/Verify", new { phone = Users.Booker, code })).EnsureSuccessStatusCode()
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
