namespace Fbs.WebApi.Auth.WorkOS;

public sealed class WorkOSOptions
{
    public const string DefaultApiBaseUrl = "https://api.workos.com";

    /// <summary>
    /// The client ID of the WorkOS environment, <c>client_</c> and then letters and digits, which is in the issuer of its access
    /// tokens and in where its keys are. Signing in with WorkOS is off without it.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The <c>iss</c> of access tokens, if not <c>https://api.workos.com/user_management/{ClientId}</c>, as with a custom
    /// authentication domain. A token from another WorkOS environment, or another app in it, has another one.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>Where the keys are, if not <c>https://api.workos.com/sso/jwks/{ClientId}</c>.</summary>
    public string? JwksUrl { get; set; }

    /// <summary>The keys themselves, for when they can't be fetched, as in tests. Used instead of <see cref="JwksUrl"/>.</summary>
    public string? JwksJson { get; set; }

    /// <summary>
    /// The secret of the webhook endpoint in WorkOS, which is what shows that a webhook came from WorkOS. Webhooks are not
    /// accepted without it.
    /// </summary>
    public string? WebhookSecret { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId);

    public string ExpectedIssuer => (Issuer ?? $"{DefaultApiBaseUrl}/user_management/{ClientId}").TrimEnd('/');

    public string ExpectedJwksUrl => JwksUrl ?? $"{DefaultApiBaseUrl}/sso/jwks/{ClientId}";
}
