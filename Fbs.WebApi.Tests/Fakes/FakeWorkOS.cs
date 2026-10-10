using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fbs.WebApi.Auth.WorkOS;

namespace Fbs.WebApi.Tests.Fakes;

/// <summary>
/// Stands in for WorkOS's User Management API, as far as moving people from Clerk uses it: users found by external ID or email,
/// made, and changed, with what was set on them kept to be looked at.
/// </summary>
public sealed class FakeWorkOS : HttpMessageHandler
{
    public const string ApiKey = "sk_test_fake";

    public sealed class StoredUser
    {
        public required string Id { get; init; }
        public required string Email { get; set; }
        public bool EmailVerified { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? ExternalId { get; set; }
        public string? PasswordHash { get; set; }
        public string? PasswordHashType { get; set; }
        public DateTimeOffset? LastSignInAt { get; set; }
    }

    private readonly List<StoredUser> _users = [];

    public IReadOnlyList<StoredUser> Users => _users;

    /// <summary>Every request, as method and path, to see that nothing was changed.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>How many of the next requests are answered with 429, with Retry-After 0.</summary>
    public int TooManyRequests { get; set; }

    public WorkOSUsers Client()
    {
        var http = new HttpClient(this) { BaseAddress = new Uri("https://api.workos.test/") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", ApiKey);
        return new WorkOSUsers(http);
    }

    public StoredUser Add(string email, string? externalId = null, DateTimeOffset? lastSignInAt = null)
    {
        var user = new StoredUser { Id = WorkOSId(), Email = email, ExternalId = externalId, LastSignInAt = lastSignInAt, EmailVerified = true };
        _users.Add(user);
        return user;
    }

    public StoredUser? ByExternalId(string externalId) => _users.SingleOrDefault(u => u.ExternalId == externalId);

    private static string WorkOSId() => $"user_01{Guid.NewGuid().ToString("N").ToUpperInvariant()}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Requests.Add($"{request.Method} {path}");
        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: ApiKey })
        {
            return Error(HttpStatusCode.Unauthorized, "unauthorized");
        }

        if (TooManyRequests > 0)
        {
            TooManyRequests--;
            var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            tooMany.Headers.RetryAfter = new(TimeSpan.Zero);
            return tooMany;
        }

        const string byExternalId = "user_management/users/external_id/";
        if (request.Method == HttpMethod.Get && path.StartsWith(byExternalId, StringComparison.Ordinal))
        {
            var found = ByExternalId(Uri.UnescapeDataString(path[byExternalId.Length..]));
            return found is null ? Error(HttpStatusCode.NotFound, "entity_not_found") : Json(found);
        }

        if (request.Method == HttpMethod.Get && path == "user_management/users")
        {
            var email = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["email"];
            return Json(new { data = _users.Where(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)).Select(Shape), list_metadata = new { } }, shaped: true);
        }

        var body = request.Content is null ? null : await request.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        if (request.Method == HttpMethod.Post && path == "user_management/users")
        {
            var email = (string)body!["email"]!;
            if (_users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                return Error(HttpStatusCode.UnprocessableEntity, "email_not_available");
            }

            var user = new StoredUser { Id = WorkOSId(), Email = email };
            Apply(user, body);
            _users.Add(user);
            return Json(user, HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Put && path.StartsWith("user_management/users/", StringComparison.Ordinal))
        {
            var user = _users.SingleOrDefault(u => u.Id == path["user_management/users/".Length..]);
            if (user is null)
            {
                return Error(HttpStatusCode.NotFound, "entity_not_found");
            }

            if (body?["external_id"] is { } externalId && _users.Any(u => u != user && u.ExternalId == (string)externalId!))
            {
                return Error(HttpStatusCode.UnprocessableEntity, "external_id_not_available");
            }

            Apply(user, body!);
            return Json(user);
        }

        return Error(HttpStatusCode.NotFound, "no_such_route");
    }

    private static void Apply(StoredUser user, JsonObject body)
    {
        if (body["email"] is { } email)
        {
            user.Email = (string)email!;
        }

        if (body["email_verified"] is { } verified)
        {
            user.EmailVerified = (bool)verified!;
        }

        if (body["first_name"] is { } first)
        {
            user.FirstName = (string?)first;
        }

        if (body["last_name"] is { } last)
        {
            user.LastName = (string?)last;
        }

        if (body["external_id"] is { } externalId)
        {
            user.ExternalId = (string?)externalId;
        }

        if (body["password_hash"] is { } hash)
        {
            user.PasswordHash = (string?)hash;
            user.PasswordHashType = (string?)body["password_hash_type"];
        }
    }

    private static object Shape(StoredUser user) =>
        new
        {
            @object = "user",
            id = user.Id,
            email = user.Email,
            email_verified = user.EmailVerified,
            first_name = user.FirstName,
            last_name = user.LastName,
            external_id = user.ExternalId,
            last_sign_in_at = user.LastSignInAt?.ToString("O"),
            profile_picture_url = (string?)null,
            created_at = "2026-10-01T00:00:00.000Z",
            updated_at = "2026-10-01T00:00:00.000Z",
            metadata = new { },
        };

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK, bool shaped = false) =>
        new(status) { Content = JsonContent.Create(shaped || value is not StoredUser user ? value : Shape(user)) };

    private static HttpResponseMessage Error(HttpStatusCode status, string code) =>
        new(status) { Content = JsonContent.Create(new { code, message = $"The fake said {code}" }) };
}
