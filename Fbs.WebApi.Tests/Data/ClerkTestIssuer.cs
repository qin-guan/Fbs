using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
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

    /// <summary>The signing secret of the webhook endpoint, as Clerk shows it.</summary>
    public static readonly string WebhookSecret = "whsec_" + Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(i * 7 + 3)).ToArray());

    /// <summary>The headers Svix sends with a webhook, signed the way it does unless told otherwise.</summary>
    public static Dictionary<string, string> WebhookHeaders(string id, string body, DateTimeOffset? sentAt = null, string? secret = null)
    {
        var timestamp = (sentAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var key = Convert.FromBase64String((secret ?? WebhookSecret)["whsec_".Length..]);
        var signature = Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}")));
        return new() { ["svix-id"] = id, ["svix-timestamp"] = timestamp, ["svix-signature"] = $"v1,{signature}" };
    }

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
    private readonly List<HttpClient> _clients = [];

    public ClerkTestIssuer Clerk { get; } = new();

    public static string NewUserId() => $"user_{Guid.NewGuid():N}";

    public static string NewSlug() => $"org-{Guid.NewGuid():N}"[..20];

    /// <summary>A client signed in as the person with the Clerk user ID, which is disposed with the factory.</summary>
    public HttpClient ClientFor(string userId, string? name = "Some One")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Clerk.Token(userId, $"{userId}@example.com", name));
        _clients.Add(client);
        return client;
    }

    /// <summary>An organisation made the way anyone would, through the API, by someone who is then its admin.</summary>
    public async Task<TestOrg> CreateOrgAsync(string founderName = "Founder", string orgName = "Test Org")
    {
        var founder = ClientFor(NewUserId(), founderName);
        var slug = NewSlug();
        (await founder.PostAsJsonAsync("/Tenants", new { name = orgName, slug, timeZone = "Asia/Singapore" })).EnsureSuccessStatusCode();
        return new TestOrg(this, slug, Db.Queryable<Tenant>().First(t => t.Slug == slug).Id, founder);
    }

    public async Task<Guid> AccountIdOfAsync(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/Me")).GetProperty("id").GetGuid();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clients.ForEach(c => c.Dispose());
        }

        base.Dispose(disposing);
    }

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
        builder.UseSetting("Clerk:WebhookSecret", ClerkTestIssuer.WebhookSecret);
    }
}

/// <summary>An organisation made for a test, and its founder, who is its admin.</summary>
public sealed class TestOrg(ClerkFbsApiFactory factory, string slug, Guid tenantId, HttpClient founder)
{
    public string Slug { get; } = slug;

    public Guid TenantId { get; } = tenantId;

    public HttpClient Admin { get; } = founder;

    /// <summary>Someone signed in who belongs to the organisation, made one the way an invite would, for tests that aren't about how they got in.</summary>
    public async Task<(HttpClient Client, Guid MemberId)> AddMemberAsync(
        string name = "A Member",
        MemberRole role = MemberRole.Member,
        MemberStatus status = MemberStatus.Active,
        Guid? unitId = null,
        string? phone = null
    )
    {
        var client = factory.ClientFor(ClerkFbsApiFactory.NewUserId(), name);
        var accountId = await factory.AccountIdOfAsync(client);
        var id = Guid.NewGuid();
        factory.Db.Insertable(
                new TenantMember
                {
                    Id = id,
                    TenantId = TenantId,
                    UserId = accountId,
                    DisplayName = name,
                    Role = role,
                    Status = status,
                    UnitId = unitId,
                    Phone = phone,
                }
            )
            .ExecuteCommand();
        return (client, id);
    }

    public Guid AddFacility(string name, bool availableToAll = true, params Guid[] unitIds)
    {
        var id = Guid.NewGuid();
        factory.Db.Insertable(new Fbs.WebApi.Data.Entities.Facility { Id = id, TenantId = TenantId, Name = name, AvailableToAll = availableToAll }).ExecuteCommand();
        foreach (var unitId in unitIds)
        {
            factory.Db.Insertable(new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = TenantId, FacilityId = id, UnitId = unitId }).ExecuteCommand();
        }

        return id;
    }

    public Guid AddUnit(string name)
    {
        var id = Guid.NewGuid();
        factory.Db.Insertable(new Fbs.WebApi.Data.Entities.Unit { Id = id, TenantId = TenantId, Name = name }).ExecuteCommand();
        return id;
    }
}
