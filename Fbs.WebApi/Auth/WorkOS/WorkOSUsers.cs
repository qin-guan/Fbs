using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fbs.WebApi.Auth.WorkOS;

/// <summary>A user as WorkOS keeps them: only what moving people from Clerk needs.</summary>
public sealed record WorkOSUser(
    string Id,
    string Email,
    bool EmailVerified,
    string? FirstName,
    string? LastName,
    string? ExternalId,
    DateTimeOffset? LastSignInAt
);

/// <summary>What is set on a user that is made or changed. What is left null is not sent, and stays as it is.</summary>
public sealed record WorkOSUserChanges
{
    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public bool? EmailVerified { get; init; }

    public string? ExternalId { get; init; }

    /// <summary>A hash of their password, so they keep it without WorkOS ever knowing it.</summary>
    public string? PasswordHash { get; init; }

    /// <summary>What made <see cref="PasswordHash"/>, such as <c>bcrypt</c>.</summary>
    public string? PasswordHashType { get; init; }
}

/// <summary>What WorkOS said when it refused a request.</summary>
public sealed class WorkOSException(HttpStatusCode status, string? code, string? message)
    : Exception($"WorkOS answered {(int)status}{(code is null ? "" : $" ({code})")}{(message is null ? "" : $": {message}")}")
{
    public HttpStatusCode Status { get; } = status;

    public string? Code { get; } = code;
}

/// <summary>
/// WorkOS's User Management API, for <c>import-clerk-users</c>: the API itself never calls WorkOS, as what it needs is in the access
/// token. Needs the environment's API key.
/// </summary>
/// <remarks>
/// A request WorkOS says is one too many is tried again after as long as it says, or a little longer each time if it doesn't say.
/// </remarks>
public sealed class WorkOSUsers(HttpClient http)
{
    public const int MaxAttempts = 6;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <returns>The user with the external ID, or null if there is none.</returns>
    public async Task<WorkOSUser?> GetByExternalIdAsync(string externalId, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"user_management/users/external_id/{Uri.EscapeDataString(externalId)}"), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadAsync<WorkOSUser>(response, ct);
    }

    /// <returns>The users with the email address, which is none or one.</returns>
    public async Task<IReadOnlyList<WorkOSUser>> FindByEmailAsync(string email, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"user_management/users?email={Uri.EscapeDataString(email)}&limit=10"), ct);
        return (await ReadAsync<UserList>(response, ct)).Data;
    }

    public async Task<WorkOSUser> CreateAsync(WorkOSUserChanges user, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "user_management/users") { Content = JsonContent.Create(user, options: Json) }, ct);
        return await ReadAsync<WorkOSUser>(response, ct);
    }

    public async Task<WorkOSUser> UpdateAsync(string userId, WorkOSUserChanges changes, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"user_management/users/{Uri.EscapeDataString(userId)}") { Content = JsonContent.Create(changes, options: Json) }, ct);
        return await ReadAsync<WorkOSUser>(response, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var message = request();
            var response = await http.SendAsync(message, ct);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == MaxAttempts)
            {
                return response;
            }

            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            response.Dispose();
            await Task.Delay(wait, ct);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            string? code = null;
            string? message = null;
            try
            {
                var error = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            }
            catch (JsonException)
            {
                // Not one WorkOS wrote
            }

            throw new WorkOSException(response.StatusCode, code, message);
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new WorkOSException(response.StatusCode, null, "An empty answer");
    }

    private sealed record UserList(List<WorkOSUser> Data);
}
