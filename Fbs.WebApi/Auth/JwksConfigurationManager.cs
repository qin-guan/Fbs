using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Auth;

/// <summary>The keys a sign-in provider signs tokens with, fetched from where it publishes them and kept for an hour, or read from the settings.</summary>
internal sealed class JwksConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly string _issuer;
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _static;
    private readonly ConfigurationManager<JsonWebKeySet>? _keys;

    /// <param name="issuer">What the tokens' <c>iss</c> is.</param>
    /// <param name="jwksUrl">Where the keys are.</param>
    /// <param name="jwksJson">The keys themselves, for when they can't be fetched, as in tests. Used instead of <paramref name="jwksUrl"/>.</param>
    /// <param name="httpClient">What fetches them.</param>
    public JwksConfigurationManager(string issuer, string jwksUrl, string? jwksJson, HttpClient httpClient)
    {
        _issuer = issuer;
        if (!string.IsNullOrWhiteSpace(jwksJson))
        {
            _static = new StaticConfigurationManager<OpenIdConnectConfiguration>(Configuration(issuer, new JsonWebKeySet(jwksJson)));
            return;
        }

        _keys = new ConfigurationManager<JsonWebKeySet>(jwksUrl, new JwksRetriever(), new HttpDocumentRetriever(httpClient) { RequireHttps = true })
        {
            AutomaticRefreshInterval = TimeSpan.FromHours(1),
            // A token signed with a key that isn't there asks for the keys again, but not more than this often
            RefreshInterval = TimeSpan.FromSeconds(30),
        };
    }

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        if (_static is not null)
        {
            return await _static.GetConfigurationAsync(cancel);
        }

        return Configuration(_issuer, await _keys!.GetConfigurationAsync(cancel));
    }

    public void RequestRefresh()
    {
        _static?.RequestRefresh();
        _keys?.RequestRefresh();
    }

    private static OpenIdConnectConfiguration Configuration(string issuer, JsonWebKeySet keys)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = issuer, JsonWebKeySet = keys };
        foreach (var key in keys.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }

    private sealed class JwksRetriever : IConfigurationRetriever<JsonWebKeySet>
    {
        public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel) =>
            new(await retriever.GetDocumentAsync(address, cancel));
    }
}
