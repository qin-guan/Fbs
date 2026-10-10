using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// Stands in for WorkOS: makes the keys the API is told to trust, and access tokens signed with them, with the claims the JWT
/// template adds.
/// </summary>
public sealed class WorkOSTestIssuer
{
    public const string ClientId = "client_01TEST";
    public const string Issuer = $"https://api.workos.com/user_management/{ClientId}";

    /// <summary>The secret of the webhook endpoint, as WorkOS shows it.</summary>
    public const string WebhookSecret = "workos-test-webhook-secret";

    /// <summary>The header WorkOS sends with a webhook, signed the way it does unless told otherwise.</summary>
    public static Dictionary<string, string> WebhookHeaders(string body, DateTimeOffset? sentAt = null, string? secret = null)
    {
        var timestamp = (sentAt ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds().ToString();
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret ?? WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
        return new() { ["WorkOS-Signature"] = $"t={timestamp}, v1={signature}" };
    }

    private readonly RSA _rsa = RSA.Create(2048);

    public string KeyId { get; } = "workos-test-key-1";

    /// <summary>The public key, as WorkOS publishes it.</summary>
    public string JwksJson
    {
        get
        {
            var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = KeyId });
            key.Use = "sig";
            key.Alg = SecurityAlgorithms.RsaSha256;
            return JsonSerializer.Serialize(new { keys = new[] { key } });
        }
    }

    /// <summary>An access token the way WorkOS makes them, with the JWT template's claims, unless told otherwise.</summary>
    /// <param name="clerkUserId">The user's external ID, which the import sets to their Clerk user ID.</param>
    public string Token(
        string userId,
        string? email = "someone@example.com",
        string? givenName = "Some",
        string? familyName = "One",
        string? clerkUserId = null,
        string? audience = null,
        string issuer = Issuer,
        TimeSpan? validFor = null,
        SecurityKey? signWith = null
    )
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["sid"] = "session_test", ["jti"] = Guid.NewGuid().ToString("N") };
        if (email is not null)
        {
            claims["email"] = email;
        }

        if (givenName is not null)
        {
            claims["given_name"] = givenName;
        }

        if (familyName is not null)
        {
            claims["family_name"] = familyName;
        }

        if (clerkUserId is not null)
        {
            claims["clerk_user_id"] = clerkUserId;
        }

        if (audience is not null)
        {
            claims["aud"] = audience;
        }

        var lifetime = validFor ?? TimeSpan.FromMinutes(5);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", userId)]),
            IssuedAt = now,
            // A token that has already expired needs to start before it ends
            NotBefore = lifetime < TimeSpan.Zero ? now + lifetime - TimeSpan.FromMinutes(1) : now,
            Expires = now + lifetime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signWith ?? new RsaSecurityKey(_rsa) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static string NewUserId() => $"user_01{Guid.NewGuid().ToString("N").ToUpperInvariant()}";

    public static void Configure(IWebHostBuilder builder, WorkOSTestIssuer issuer)
    {
        builder.UseSetting("WorkOS:ClientId", ClientId);
        builder.UseSetting("WorkOS:JwksJson", issuer.JwksJson);
        builder.UseSetting("WorkOS:WebhookSecret", WebhookSecret);
    }
}

/// <summary>The API with signing in with WorkOS on, and Clerk off, as it is once everybody has moved.</summary>
public class WorkOSFbsApiFactory : DatabaseFbsApiFactory
{
    public WorkOSTestIssuer WorkOS { get; } = new();

    public HttpClient ClientWithWorkOS(string? token)
    {
        var client = CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        WorkOSTestIssuer.Configure(builder, WorkOS);
    }
}

/// <summary>The API with both Clerk and WorkOS on, as it is while people move from one to the other.</summary>
public class MovingFbsApiFactory : ClerkFbsApiFactory
{
    public WorkOSTestIssuer WorkOS { get; } = new();

    public HttpClient ClientWithWorkOS(string? token)
    {
        var client = CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        WorkOSTestIssuer.Configure(builder, WorkOS);
    }
}
