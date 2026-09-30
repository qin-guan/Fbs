using System.Security.Claims;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Claims;
using Fbs.WebApi.TelegramLinks;
using Fbs.WebApi.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Auth.Clerk;

/// <summary>
/// Marks an endpoint that is for people who sign in with Clerk, which only exists when that is turned on, as
/// it needs the database and accounts to be there.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresClerkAttribute : Attribute;

/// <summary>
/// Signing in with Clerk: the API accepts the session token the browser gets from Clerk as a bearer token,
/// and checks it against Clerk's keys.
/// </summary>
public static class ClerkAuthentication
{
    public const string Scheme = "Clerk";

    private const string HttpClientName = "clerk-jwks";

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetSection("Clerk").Get<ClerkOptions>()?.Enabled == true;

    public static IServiceCollection AddClerkAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ClerkOptions>()
            .Bind(configuration.GetSection("Clerk"))
            .Validate(
                options => !options.Enabled || options.AuthorizedParties.Count > 0,
                "Clerk:AuthorizedParties is required: the origins of the apps that may use a session token."
            )
            .Validate(
                options => !options.Enabled || Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer) && issuer.Scheme == Uri.UriSchemeHttps,
                "Clerk:Issuer has to be the https address of Clerk's Frontend API."
            )
            .ValidateOnStart();
        services.AddHttpClient(HttpClientName);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentAccount, CurrentAccount>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantBookings>();
        services.AddScoped<TenantQuotas>();
        services.AddSingleton<TelegramBotIdentity>();
        services.AddScoped<TelegramLinker>();
        services.AddScoped<MemberClaims>();
        services.AddScoped<AccountErasure>();
        services.AddScoped<MemberPromotions>();
        services.Configure<TenantLimits>(configuration.GetSection("Limits"));

        services.AddAuthentication().AddJwtBearer(Scheme, _ => { });
        services
            .AddOptions<JwtBearerOptions>(Scheme)
            .Configure<IHttpClientFactory, IOptions<ClerkOptions>>(
                (jwt, httpClientFactory, clerk) =>
                {
                    var options = clerk.Value;
                    var issuer = options.Issuer!.TrimEnd('/');

                    // A key is not looked for in the claims, so a token can't make one up
                    jwt.MapInboundClaims = false;
                    jwt.SaveToken = false;
                    jwt.ConfigurationManager = new ClerkConfigurationManager(issuer, options, httpClientFactory.CreateClient(HttpClientName));
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = issuer,
                        // Clerk session tokens have no audience: the app they are for is their azp
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ValidateIssuerSigningKey = true,
                        // Whatever a token says its algorithm is, only this one is accepted, which rules out "none"
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                        ClockSkew = TimeSpan.FromSeconds(30),
                        NameClaimType = "name",
                        AuthenticationType = Scheme,
                    };
                    jwt.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            var azp = context.Principal?.FindFirstValue("azp");
                            if (azp is null || !options.AuthorizedParties.Contains(azp, StringComparer.OrdinalIgnoreCase))
                            {
                                context.Fail("The token was not issued to this app.");
                            }

                            if (context.Principal?.FindFirstValue("sub") is not { Length: > 0 })
                            {
                                context.Fail("The token is not for anyone.");
                            }

                            return Task.CompletedTask;
                        },
                    };
                }
            );

        return services;
    }

    /// <summary>Clerk's keys, fetched from its Frontend API and kept for an hour, or read from the settings.</summary>
    private sealed class ClerkConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly string _issuer;
        private readonly IConfigurationManager<OpenIdConnectConfiguration>? _static;
        private readonly ConfigurationManager<JsonWebKeySet>? _keys;

        public ClerkConfigurationManager(string issuer, ClerkOptions options, HttpClient httpClient)
        {
            _issuer = issuer;
            if (!string.IsNullOrWhiteSpace(options.JwksJson))
            {
                _static = new StaticConfigurationManager<OpenIdConnectConfiguration>(Configuration(issuer, new JsonWebKeySet(options.JwksJson)));
                return;
            }

            _keys = new ConfigurationManager<JsonWebKeySet>(
                options.JwksUrl ?? $"{issuer}/.well-known/jwks.json",
                new JwksRetriever(),
                new HttpDocumentRetriever(httpClient) { RequireHttps = true }
            )
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
    }

    private sealed class JwksRetriever : IConfigurationRetriever<JsonWebKeySet>
    {
        public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel) =>
            new(await retriever.GetDocumentAsync(address, cancel));
    }
}
