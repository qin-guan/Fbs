using System.Text.Json;
using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.RateLimiting;
using Fbs.WebApi.Telemetry;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Endpoints.Webhooks.Clerk.Post;

/// <summary>
/// Clerk telling us what happened to somebody's account. Anyone can reach this, so nothing is done unless the webhook is signed with
/// the secret only Clerk has, and one that is old is not accepted.
/// </summary>
/// <remarks>
/// Webhooks can be late, twice, or out of order, so what is done for one has to be fine if it is done again, and nothing depends on
/// it having arrived: sessions are what say who somebody is, and this only tidies up after an account is deleted. Once somebody has
/// moved to WorkOS, a user deleted in Clerk leaves their account as it is (see <see cref="AccountErasure.EraseClerkUserAsync"/>).
/// </remarks>
[RequiresClerk]
public class Endpoint(IOptions<ClerkOptions> options, AccountErasure erasure, ILogger<Endpoint> logger) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/webhooks/clerk");
        AllowAnonymous();
        Options(x => x.RequireRateLimiting(RateLimitPolicies.Webhook));
        // The body is read as it was sent, as that is what is signed
        Description(d => d.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogWarning("A Clerk webhook arrived but Clerk:WebhookSecret is not set, so it was not accepted.");
            CountWebhook("unknown", "not_configured");
            await Send.ResponseAsync(null, StatusCodes.Status503ServiceUnavailable, ct);
            return;
        }

        var body = await WebhookBody.ReadAsync(HttpContext.Request, ct);
        if (body is null)
        {
            await Send.ResponseAsync(null, StatusCodes.Status413PayloadTooLarge, ct);
            return;
        }

        var headers = HttpContext.Request.Headers;
        if (!SvixSignature.IsValid(secret, headers["svix-id"], headers["svix-timestamp"], headers["svix-signature"], body, DateTimeOffset.UtcNow))
        {
            logger.LogWarning("Rejected a Clerk webhook without a valid signature.");
            CountWebhook("unknown", "invalid_signature");
            await Send.UnauthorizedAsync(ct);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            CountWebhook(root.TryGetProperty("type", out var kind) ? kind.GetString() : null, "accepted");
            if (root.TryGetProperty("type", out var type) && type.GetString() == "user.deleted" && root.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } clerkUserId)
            {
                var erased = await erasure.EraseClerkUserAsync(clerkUserId, ct);
                logger.LogInformation("Clerk user was deleted, and {Result}.", erased ? "their account was erased" : "there was nothing to erase");
            }
        }
        catch (JsonException)
        {
            // Signed, so it is from Clerk, but not something to do anything about
            logger.LogWarning("A Clerk webhook could not be read.");
        }

        await Send.OkAsync(ct);
    }

    /// <summary>The type is only one of those Clerk has, and anything else is "other", so what is sent can't make up lines to count.</summary>
    private static void CountWebhook(string? type, string result) =>
        FbsMetrics.Webhooks.Add(
            1,
            new KeyValuePair<string, object?>("provider", "clerk"),
            new KeyValuePair<string, object?>("type", type is "user.created" or "user.updated" or "user.deleted" or "session.created" or "session.ended" or "session.removed" or "session.revoked" or "unknown" ? type : "other"),
            new KeyValuePair<string, object?>("result", result)
        );
}
