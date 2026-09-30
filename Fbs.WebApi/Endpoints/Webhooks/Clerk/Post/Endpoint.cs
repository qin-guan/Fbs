using System.Text;
using System.Text.Json;
using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Endpoints.Webhooks.Clerk.Post;

/// <summary>
/// Clerk telling us what happened to somebody's account. Anyone can reach this, so nothing is done unless the webhook is signed with
/// the secret only Clerk has, and one that is old is not accepted.
/// </summary>
/// <remarks>
/// Webhooks can be late, twice, or out of order, so what is done for one has to be fine if it is done again, and nothing depends on
/// it having arrived: sessions are what say who somebody is, and this only tidies up after an account is deleted.
/// </remarks>
[RequiresClerk]
public class Endpoint(IOptions<ClerkOptions> options, AccountErasure erasure, ILogger<Endpoint> logger) : EndpointWithoutRequest
{
    /// <summary>What Clerk sends for an account is small, so anything larger isn't from Clerk.</summary>
    private const int MaxBodyBytes = 256 * 1024;

    public override void Configure()
    {
        Post("/webhooks/clerk");
        AllowAnonymous();
        // The body is read as it was sent, as that is what is signed
        Description(d => d.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogWarning("A Clerk webhook arrived but Clerk:WebhookSecret is not set, so it was not accepted.");
            await Send.ResponseAsync(null, StatusCodes.Status503ServiceUnavailable, ct);
            return;
        }

        if (HttpContext.Request.ContentLength is > MaxBodyBytes)
        {
            await Send.ResponseAsync(null, StatusCodes.Status413PayloadTooLarge, ct);
            return;
        }

        var body = await ReadBodyAsync(ct);
        if (body is null)
        {
            await Send.ResponseAsync(null, StatusCodes.Status413PayloadTooLarge, ct);
            return;
        }

        var headers = HttpContext.Request.Headers;
        if (!SvixSignature.IsValid(secret, headers["svix-id"], headers["svix-timestamp"], headers["svix-signature"], body, DateTimeOffset.UtcNow))
        {
            logger.LogWarning("Rejected a Clerk webhook without a valid signature.");
            await Send.UnauthorizedAsync(ct);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "user.deleted" && root.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } clerkUserId)
            {
                var erased = await erasure.EraseAsync(clerkUserId, ct);
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

    private async Task<string?> ReadBodyAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await HttpContext.Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
