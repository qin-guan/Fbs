using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Tests.Data;

namespace Fbs.WebApi.Tests;

/// <summary>Whether a webhook is from Clerk: signed over what was sent, with the secret, and recently.</summary>
public class SvixSignatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private const string Body = """{"type":"user.deleted","data":{"id":"user_1","deleted":true}}""";

    private static bool Valid(Dictionary<string, string> headers, string body = Body, string? secret = null, DateTimeOffset? now = null) =>
        SvixSignature.IsValid(secret ?? ClerkTestIssuer.WebhookSecret, headers.GetValueOrDefault("svix-id"), headers.GetValueOrDefault("svix-timestamp"), headers.GetValueOrDefault("svix-signature"), body, now ?? Now);

    [Test]
    public async Task What_is_signed_with_the_secret_over_what_was_sent_is_valid()
    {
        await Assert.That(Valid(ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now))).IsTrue();
    }

    [Test]
    public async Task Any_of_several_signatures_will_do_as_there_are_two_while_a_secret_is_being_changed()
    {
        var headers = ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now);
        headers["svix-signature"] = "v1,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA= " + headers["svix-signature"];

        await Assert.That(Valid(headers)).IsTrue();
    }

    [Test]
    public async Task A_change_to_the_body_the_id_or_the_time_is_not_valid()
    {
        var headers = ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now);

        await Assert.That(Valid(headers, Body.Replace("user_1", "user_2"))).IsFalse();
        await Assert.That(Valid(new(headers) { ["svix-id"] = "msg_2" })).IsFalse();
        await Assert.That(Valid(new(headers) { ["svix-timestamp"] = (Now.ToUnixTimeSeconds() + 1).ToString() })).IsFalse();
    }

    [Test]
    public async Task Another_secret_is_not_valid()
    {
        var other = "whsec_" + Convert.ToBase64String(new byte[32]);

        await Assert.That(Valid(ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now, secret: other))).IsFalse();
        await Assert.That(Valid(ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now), secret: other)).IsFalse();
    }

    [Test]
    public async Task One_that_is_too_old_or_from_too_far_ahead_is_not_valid_and_five_minutes_either_way_is()
    {
        await Assert.That(Valid(ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now.AddMinutes(-4.9)))).IsTrue();
        await Assert.That(Valid(ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now.AddMinutes(4.9)))).IsTrue();
        await Assert.That(Valid(ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now.AddMinutes(-5.1)))).IsFalse();
        await Assert.That(Valid(ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now.AddMinutes(5.1)))).IsFalse();
    }

    [Test]
    public async Task Missing_or_malformed_headers_and_secrets_are_not_valid_and_do_not_throw()
    {
        var good = ClerkTestIssuer.WebhookHeaders("msg_1", Body, Now);

        await Assert.That(Valid([])).IsFalse();
        await Assert.That(Valid(new(good) { ["svix-timestamp"] = "yesterday" })).IsFalse();
        await Assert.That(Valid(new(good) { ["svix-signature"] = "v2," + good["svix-signature"][3..] })).IsFalse();
        await Assert.That(Valid(new(good) { ["svix-signature"] = "v1,not base64!" })).IsFalse();
        await Assert.That(Valid(new(good) { ["svix-signature"] = "" })).IsFalse();
        await Assert.That(Valid(good, secret: "whsec_not base64!")).IsFalse();
    }
}
