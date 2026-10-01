namespace Fbs.WebApi.Auth.Clerk;

public sealed class ClerkOptions
{
    /// <summary>
    /// Clerk's Frontend API, such as <c>https://example.clerk.accounts.dev</c>, which is the <c>iss</c> of session
    /// tokens and where their keys are. Signing in with Clerk is off without it.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// The origins of the apps that may use a session token, checked against its <c>azp</c>, so a token issued to
    /// some other app on the same Clerk instance is not accepted here.
    /// </summary>
    public List<string> AuthorizedParties { get; set; } = [];

    /// <summary>Where the keys are, if not <c>{Issuer}/.well-known/jwks.json</c>.</summary>
    public string? JwksUrl { get; set; }

    /// <summary>The keys themselves, for when they can't be fetched, as in tests. Used instead of <see cref="JwksUrl"/>.</summary>
    public string? JwksJson { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Issuer);
}
