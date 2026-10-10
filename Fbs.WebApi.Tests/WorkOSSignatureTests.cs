using Fbs.WebApi.Auth.WorkOS;
using Fbs.WebApi.Tests.Data;

namespace Fbs.WebApi.Tests;

/// <summary>Whether a webhook is from WorkOS: signed over what was sent, with the secret, and recently.</summary>
public class WorkOSSignatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
    private const string Body = """{"id":"event_1","event":"user.deleted","data":{"id":"user_01A"}}""";

    private static bool Valid(Dictionary<string, string> headers, string body = Body, string? secret = null, DateTimeOffset? now = null) =>
        WorkOSSignature.IsValid(secret ?? WorkOSTestIssuer.WebhookSecret, headers.GetValueOrDefault("WorkOS-Signature"), body, now ?? Now);

    private static Dictionary<string, string> Header(string value) => new() { ["WorkOS-Signature"] = value };

    [Test]
    public async Task What_is_signed_with_the_secret_over_what_was_sent_is_valid()
    {
        await Assert.That(Valid(WorkOSTestIssuer.WebhookHeaders(Body, Now))).IsTrue();
    }

    [Test]
    public async Task The_parts_of_the_header_can_be_spaced_and_in_either_order_and_upper_case_hex_will_do()
    {
        var parts = WorkOSTestIssuer.WebhookHeaders(Body, Now)["WorkOS-Signature"].Split(", ");

        await Assert.That(Valid(Header($"{parts[1]},{parts[0]}"))).IsTrue();
        await Assert.That(Valid(Header($"{parts[0]},  {parts[1].ToUpperInvariant().Replace("V1=", "v1=")}"))).IsTrue();
    }

    [Test]
    public async Task Any_of_several_signatures_will_do()
    {
        var parts = WorkOSTestIssuer.WebhookHeaders(Body, Now)["WorkOS-Signature"].Split(", ");

        await Assert.That(Valid(Header($"{parts[0]}, v1={new string('0', 64)}, {parts[1]}"))).IsTrue();
    }

    [Test]
    public async Task A_change_to_the_body_or_the_time_is_not_valid()
    {
        var headers = WorkOSTestIssuer.WebhookHeaders(Body, Now);
        var signature = headers["WorkOS-Signature"].Split(", ")[1];

        await Assert.That(Valid(headers, Body.Replace("user_01A", "user_01B"))).IsFalse();
        await Assert.That(Valid(Header($"t={Now.ToUnixTimeMilliseconds() + 1}, {signature}"))).IsFalse();
    }

    [Test]
    public async Task Another_secret_is_not_valid()
    {
        await Assert.That(Valid(WorkOSTestIssuer.WebhookHeaders(Body, Now, secret: "another"))).IsFalse();
        await Assert.That(Valid(WorkOSTestIssuer.WebhookHeaders(Body, Now), secret: "another")).IsFalse();
    }

    [Test]
    public async Task One_that_is_too_old_or_from_too_far_ahead_is_not_valid_and_five_minutes_either_way_is()
    {
        await Assert.That(Valid(WorkOSTestIssuer.WebhookHeaders(Body, Now.AddMinutes(-4.9)))).IsTrue();
        await Assert.That(Valid(WorkOSTestIssuer.WebhookHeaders(Body, Now.AddMinutes(4.9)))).IsTrue();
        await Assert.That(Valid(WorkOSTestIssuer.WebhookHeaders(Body, Now.AddMinutes(-5.1)))).IsFalse();
        await Assert.That(Valid(WorkOSTestIssuer.WebhookHeaders(Body, Now.AddMinutes(5.1)))).IsFalse();
    }

    [Test]
    public async Task Seconds_are_not_taken_for_milliseconds()
    {
        var signature = WorkOSTestIssuer.WebhookHeaders(Body, Now)["WorkOS-Signature"].Split(", ")[1];

        await Assert.That(Valid(Header($"t={Now.ToUnixTimeSeconds()}, {signature}"))).IsFalse();
    }

    [Test]
    public async Task Missing_or_malformed_headers_and_secrets_are_not_valid_and_do_not_throw()
    {
        var good = WorkOSTestIssuer.WebhookHeaders(Body, Now);

        await Assert.That(Valid([])).IsFalse();
        await Assert.That(Valid(Header(""))).IsFalse();
        await Assert.That(Valid(Header("t=, v1="))).IsFalse();
        await Assert.That(Valid(Header($"t={Now.ToUnixTimeMilliseconds()}"))).IsFalse();
        await Assert.That(Valid(Header("v1=abcdef"))).IsFalse();
        await Assert.That(Valid(Header($"t={Now.ToUnixTimeMilliseconds()}, v1=not-hex"))).IsFalse();
        await Assert.That(Valid(Header($"t={long.MaxValue}, v1=00"))).IsFalse();
        await Assert.That(Valid(Header($"t=-{long.MaxValue}, v1=00"))).IsFalse();
        await Assert.That(Valid(Header("garbage"))).IsFalse();
        await Assert.That(Valid(good, secret: "")).IsFalse();
    }
}
