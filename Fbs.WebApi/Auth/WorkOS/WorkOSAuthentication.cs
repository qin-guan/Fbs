using System.Security.Claims;
using Fbs.WebApi.Telemetry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Auth.WorkOS;

/// <summary>Marks an endpoint that only exists while signing in with WorkOS is on, such as its webhook.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresWorkOSAttribute : Attribute;

/// <summary>
/// Signing in with WorkOS (AuthKit): the API accepts the access token the browser gets from WorkOS as a bearer token, and checks it
/// against WorkOS's keys. Endpoints don't ask for this scheme, but for <see cref="AccountAuthentication.Scheme"/>, which sends WorkOS's
/// tokens here.
/// </summary>
/// <remarks>
/// An access token only says who somebody is (<c>sub</c>) and which session it is. What else is needed is added to it with a JWT
/// template in WorkOS: <c>email</c>, <c>given_name</c> and <c>family_name</c>, so an account can be made the first time somebody is
/// seen without asking WorkOS, and <see cref="ClerkUserIdClaim"/>, so somebody moved from Clerk is the account they had (see
/// docs/runbooks/cutover-3-workos.md).
/// </remarks>
public static class WorkOSAuthentication
{
    public const string Scheme = "WorkOS";

    /// <summary>
    /// The claim with the WorkOS user's external ID, which <c>import-clerk-users</c> sets to their Clerk user ID. Only WorkOS can
    /// put it in a token, and only somebody with the API key can set it.
    /// </summary>
    public const string ClerkUserIdClaim = "clerk_user_id";

    private const string HttpClientName = "workos-jwks";

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetSection("WorkOS").Get<WorkOSOptions>()?.Enabled == true;

    internal static IServiceCollection AddWorkOSAuthentication(this IServiceCollection services)
    {
        services
            .AddOptions<WorkOSOptions>()
            .BindConfiguration("WorkOS")
            .Validate(
                options => !options.Enabled || Uri.TryCreate(options.ExpectedIssuer, UriKind.Absolute, out var issuer) && issuer.Scheme == Uri.UriSchemeHttps,
                "WorkOS:Issuer has to be an https address."
            )
            .ValidateOnStart();
        services.AddHttpClient(HttpClientName);

        services.AddAuthentication().AddJwtBearer(Scheme, _ => { });
        services
            .AddOptions<JwtBearerOptions>(Scheme)
            .Configure<IHttpClientFactory, IOptions<WorkOSOptions>>(
                (jwt, httpClientFactory, workOS) =>
                {
                    var options = workOS.Value;
                    var issuer = options.ExpectedIssuer;

                    // A key is not looked for in the claims, so a token can't make one up
                    jwt.MapInboundClaims = false;
                    jwt.SaveToken = false;
                    jwt.ConfigurationManager = new JwksConfigurationManager(issuer, options.ExpectedJwksUrl, options.JwksJson, httpClientFactory.CreateClient(HttpClientName));
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        // The issuer has the client ID in it, which is what makes a token one for this app
                        ValidateIssuer = true,
                        ValidIssuer = issuer,
                        // Checked below instead, as a token may not have one
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
                            var audiences = context.Principal?.FindAll("aud").Select(c => c.Value).ToList() ?? [];
                            if (audiences.Count > 0 && !audiences.Contains(options.ClientId, StringComparer.Ordinal))
                            {
                                CountFailure("audience");
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
        FbsMetrics.AuthFailures.Add(1, new KeyValuePair<string, object?>("reason", reason), new KeyValuePair<string, object?>("provider", "workos"));
}
