using System.Text.Json;
using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Auth.WorkOS;
using Fbs.WebApi.RateLimiting;
using Fbs.WebApi.Telemetry;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Endpoints.Webhooks.WorkOS.Post;

/// <summary>
/// WorkOS telling us what happened to somebody's account. Anyone can reach this, so nothing is done unless the webhook is signed with
/// the secret only WorkOS has, and one that is old is not accepted.
/// </summary>
/// <remarks>
/// Webhooks can be late, twice, or out of order, so what is done for one has to be fine if it is done again, and nothing depends on
/// it having arrived: access tokens are what say who somebody is, and this only tidies up after an account is deleted.
/// </remarks>
[RequiresWorkOS]
public class Endpoint(IOptions<WorkOSOptions> options, AccountErasure erasure, ILogger<Endpoint> logger) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/webhooks/workos");
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
            logger.LogWarning("A WorkOS webhook arrived but WorkOS:WebhookSecret is not set, so it was not accepted.");
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

        if (!WorkOSSignature.IsValid(secret, HttpContext.Request.Headers["WorkOS-Signature"], body, DateTimeOffset.UtcNow))
        {
            logger.LogWarning("Rejected a WorkOS webhook without a valid signature.");
            CountWebhook("unknown", "invalid_signature");
            await Send.UnauthorizedAsync(ct);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var type = root.TryGetProperty("event", out var kind) && kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
            CountWebhook(type, "accepted");
            if (type == "user.deleted" && root.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } workOSUserId)
            {
                var erased = await erasure.EraseWorkOSUserAsync(workOSUserId, ct);
                logger.LogInformation("WorkOS user was deleted, and {Result}.", erased ? "their account was erased" : "there was nothing to erase");
            }
        }
        catch (JsonException)
        {
            // Signed, so it is from WorkOS, but not something to do anything about
            logger.LogWarning("A WorkOS webhook could not be read.");
        }

        await Send.OkAsync(ct);
    }

    /// <summary>The type is only one of those WorkOS has, and anything else is "other", so what is sent can't make up lines to count.</summary>
    private static void CountWebhook(string? type, string result) =>
        FbsMetrics.Webhooks.Add(
            1,
            new KeyValuePair<string, object?>("provider", "workos"),
            new KeyValuePair<string, object?>("type", type is "user.created" or "user.updated" or "user.deleted" or "session.created" or "session.revoked" or "unknown" ? type : "other"),
            new KeyValuePair<string, object?>("result", result)
        );
}
