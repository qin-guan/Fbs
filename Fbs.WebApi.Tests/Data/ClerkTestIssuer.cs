using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// Stands in for Clerk: makes the keys the API is told to trust, and session tokens signed with them.
/// </summary>
public sealed class ClerkTestIssuer
{
    public const string Issuer = "https://clerk.test.example";
    public const string App = "https://app.test.example";

    private readonly RSA _rsa = RSA.Create(2048);

    public string KeyId { get; } = "test-key-1";

    /// <summary>The public key, as Clerk publishes it.</summary>
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

    /// <summary>A session token the way Clerk makes them, unless told otherwise.</summary>
    public string Token(
        string userId,
        string? email = "someone@example.com",
        string? name = "Some One",
        string? authorizedParty = App,
        string issuer = Issuer,
        TimeSpan? validFor = null,
        SecurityKey? signWith = null,
        string algorithm = SecurityAlgorithms.RsaSha256
    )
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["sid"] = "sess_test" };
        if (authorizedParty is not null)
        {
            claims["azp"] = authorizedParty;
        }

        if (email is not null)
        {
            claims["email"] = email;
        }

        if (name is not null)
        {
            claims["name"] = name;
        }

        var lifetime = validFor ?? TimeSpan.FromMinutes(1);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", userId)]),
            IssuedAt = now,
            // A token that has already expired needs to start before it ends
            NotBefore = lifetime < TimeSpan.Zero ? now + lifetime - TimeSpan.FromMinutes(1) : now,
            Expires = now + lifetime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signWith ?? new RsaSecurityKey(_rsa) { KeyId = KeyId }, algorithm),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Keys of someone else's, which a token signed with them must not be accepted for.</summary>
    public static SecurityKey AnotherKey() => new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key-1" };
}

/// <summary>The API with signing in with Clerk on, as well as the phone number the tests otherwise use.</summary>
public class ClerkFbsApiFactory : DatabaseFbsApiFactory
{
    public ClerkTestIssuer Clerk { get; } = new();

    public HttpClient CreateClientSignedInAs(string userId, string? email = "someone@example.com", string? name = "Some One")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Clerk.Token(userId, email, name));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Clerk:Issuer", ClerkTestIssuer.Issuer);
        builder.UseSetting("Clerk:AuthorizedParties:0", ClerkTestIssuer.App);
        builder.UseSetting("Clerk:JwksJson", Clerk.JwksJson);
    }
}
