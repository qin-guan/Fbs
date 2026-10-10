using System.Security.Claims;
using Fbs.WebApi.Telemetry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Auth.Clerk;

/// <summary>Marks an endpoint that only exists while signing in with Clerk is on, such as its webhook.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresClerkAttribute : Attribute;

/// <summary>
/// Signing in with Clerk: the API accepts the session token the browser gets from Clerk as a bearer token,
/// and checks it against Clerk's keys. Endpoints don't ask for this scheme, but for <see cref="AccountAuthentication.Scheme"/>,
/// which sends Clerk's tokens here.
/// </summary>
public static class ClerkAuthentication
{
    public const string Scheme = "Clerk";

    private const string HttpClientName = "clerk-jwks";

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetSection("Clerk").Get<ClerkOptions>()?.Enabled == true;

    internal static IServiceCollection AddClerkAuthentication(this IServiceCollection services)
    {
        services
            .AddOptions<ClerkOptions>()
            .BindConfiguration("Clerk")
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
                    jwt.ConfigurationManager = new JwksConfigurationManager(
                        issuer,
                        options.JwksUrl ?? $"{issuer}/.well-known/jwks.json",
                        options.JwksJson,
                        httpClientFactory.CreateClient(HttpClientName)
                    );
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
                                CountFailure("azp");
                                context.Fail("The token was not issued to this app.");
                            }
                            else if (context.Principal?.FindFirstValue("sub") is not { Length: > 0 })
                            {
                                CountFailure("subject");
                                context.Fail("The token is not for anyone.");
                            }

                            return Task.CompletedTask;
                        },
                        // A request with no token isn't a failure, so it isn't counted: this is for tokens that were sent and refused
                        OnAuthenticationFailed = context =>
                        {
                            CountFailure(AccountAuthentication.ReasonOf(context.Exception));
                            return Task.CompletedTask;
                        },
                    };
                }
            );

        return services;
    }

    private static void CountFailure(string reason) =>
        FbsMetrics.AuthFailures.Add(1, new KeyValuePair<string, object?>("reason", reason), new KeyValuePair<string, object?>("provider", "clerk"));
}
