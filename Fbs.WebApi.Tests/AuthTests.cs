using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Fbs.WebApi.Endpoints.Auth;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

public abstract class AuthTests(FbsApiFactory factory)
{
    protected FbsApiFactory Factory { get; } = factory;

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

    /// <summary>Six digit codes that aren't the real one.</summary>
    private static IEnumerable<string> WrongCodes(string code, int count) =>
        Enumerable.Range(0, count + 1).Select(i => $"{i}00000").Where(c => c != code).Take(count);

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
        await Assert.That(Factory.StoredCodeCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_wrong_code_is_rejected()
    {
        var code = await RequestCodeAsync(Users.Booker);
        var wrong = code == "000000" ? "111111" : "000000";

        var response = await VerifyAsync(Users.Booker, wrong);

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(Factory.StoredCodeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_code_works_just_before_it_expires()
    {
        var code = await RequestCodeAsync(Users.Booker);
        Factory.AgeCode(Users.Booker, TimeSpan.FromMinutes(4));

        var response = await VerifyAsync(Users.Booker, code);

        response.EnsureSuccessStatusCode();
    }

    [Test]
    public async Task A_code_expires_after_five_minutes()
    {
        var code = await RequestCodeAsync(Users.Booker);
        Factory.AgeCode(Users.Booker, TimeSpan.FromMinutes(6));

        var response = await VerifyAsync(Users.Booker, code);

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
    }

    [Test]
    public async Task A_code_can_only_be_tried_five_times()
    {
        var code = await RequestCodeAsync(Users.Booker);

        foreach (var wrong in WrongCodes(code, OtpAttemptTracker.MaxAttempts))
        {
            await Assert.That(await VerifyAsync(Users.Booker, wrong)).HasStatus(HttpStatusCode.Unauthorized);
        }

        // Even the right code is refused now, so guessing can't get through by persistence
        var response = await VerifyAsync(Users.Booker, code);

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
    }

    [Test]
    public async Task Fewer_wrong_guesses_do_not_use_up_the_code()
    {
        var code = await RequestCodeAsync(Users.Booker);

        foreach (var wrong in WrongCodes(code, OtpAttemptTracker.MaxAttempts - 1))
        {
            await Assert.That(await VerifyAsync(Users.Booker, wrong)).HasStatus(HttpStatusCode.Unauthorized);
        }

        var response = await VerifyAsync(Users.Booker, code);

        response.EnsureSuccessStatusCode();
    }

    [Test]
    public async Task A_new_code_gets_a_fresh_set_of_attempts()
    {
        var code = await RequestCodeAsync(Users.Booker);
        foreach (var wrong in WrongCodes(code, OtpAttemptTracker.MaxAttempts))
        {
            await VerifyAsync(Users.Booker, wrong);
        }

        // A new code can only be asked for a minute after the last one
        Factory.AgeCode(Users.Booker, TimeSpan.FromMinutes(2));
        var newCode = await RequestCodeAsync(Users.Booker);

        var response = await VerifyAsync(Users.Booker, newCode);

        response.EnsureSuccessStatusCode();
    }
}

/// <summary>AuthTests with users, facilities, the roster and login codes in Google Sheets.</summary>
[ClassDataSource<FbsApiFactory>]
[InheritsTests]
public class GoogleAuthTests(FbsApiFactory factory) : AuthTests(factory);

/// <summary>AuthTests with users, facilities, the roster and login codes in the database.</summary>
[ClassDataSource<DatabaseFbsApiFactory>]
[InheritsTests]
// A login code belongs to a phone number, not to a tenant, and every test asks for one for the same
// number, so these can't run alongside each other, and nothing else asks for codes
[NotInParallel("login-codes")]
public class DatabaseAuthTests(DatabaseFbsApiFactory factory) : AuthTests(factory)
{
    [Before(Test)]
    public void ClearLoginCodes() => ((DatabaseFbsApiFactory)Factory).ClearLoginCodes();
}
