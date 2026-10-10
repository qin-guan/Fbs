using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Auth.WorkOS;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Claims;
using Fbs.WebApi.TelegramLinks;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fbs.WebApi.Auth;

/// <summary>
/// Marks an endpoint that is for people who sign in with an account, which only exists when signing in with Clerk or WorkOS is
/// turned on, as it needs the database and accounts to be there.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresAccountsAttribute : Attribute;

/// <summary>
/// Signing in with an account: a session token from Clerk, or an access token from WorkOS, sent as a bearer token. Both can be on at
/// once, which is how people move from one to the other without anyone being locked out: the issuer of a token says which of them
/// checks it.
/// </summary>
/// <remarks>
/// Endpoints ask for <see cref="Scheme"/>, never for Clerk or WorkOS, so which of them is on is only ever configuration.
/// </remarks>
public static class AccountAuthentication
{
    public const string Scheme = "Account";

    public static bool IsEnabled(IConfiguration configuration) => ClerkAuthentication.IsEnabled(configuration) || WorkOSAuthentication.IsEnabled(configuration);

    public static IServiceCollection AddAccountAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentAccount, CurrentAccount>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantBookings>();
        services.AddScoped<TenantQuotas>();
        services.AddScoped<AuditLog>();
        services.AddScoped<TenantDeletions>();
        services.AddSingleton<TelegramBotIdentity>();
        services.AddScoped<TelegramLinker>();
        services.AddScoped<MemberClaims>();
        services.AddScoped<AccountErasure>();
        services.AddScoped<MemberPromotions>();
        services.Configure<TenantLimits>(configuration.GetSection("Limits"));

        var clerk = ClerkAuthentication.IsEnabled(configuration);
        var workOS = WorkOSAuthentication.IsEnabled(configuration);
        if (clerk)
        {
            services.AddClerkAuthentication();
        }

        if (workOS)
        {
            services.AddWorkOSAuthentication();
        }

        services
            .AddAuthentication()
            .AddPolicyScheme(
                Scheme,
                Scheme,
                options => options.ForwardDefaultSelector = context => (clerk, workOS) switch
                {
                    (true, false) => ClerkAuthentication.Scheme,
                    (false, true) => WorkOSAuthentication.Scheme,
                    _ => IsFromClerk(context) ? ClerkAuthentication.Scheme : WorkOSAuthentication.Scheme,
                }
            );

        return services;
    }

    /// <summary>
    /// Whether a token says Clerk issued it. Nothing is checked here, as the scheme it is sent to checks all of it: this only picks
    /// which, so a token for one isn't refused by the other and counted as a failure. Anything that isn't Clerk's goes to WorkOS.
    /// </summary>
    private static bool IsFromClerk(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = header["Bearer ".Length..].Trim();
        var handler = new JsonWebTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return false;
        }

        string issuer;
        try
        {
            issuer = handler.ReadJsonWebToken(token).Issuer;
        }
        catch (ArgumentException)
        {
            return false;
        }

        var clerk = context.RequestServices.GetRequiredService<IOptions<ClerkOptions>>().Value;
        return string.Equals(issuer.TrimEnd('/'), clerk.Issuer?.TrimEnd('/'), StringComparison.Ordinal);
    }

    /// <summary>Why a token was refused, in a few words that are ours: what a token says is never what is counted.</summary>
    internal static string ReasonOf(Exception exception) =>
        exception switch
        {
            SecurityTokenExpiredException => "expired",
            SecurityTokenNotYetValidException => "not_yet_valid",
            // Before the signature, which it is a kind of
            SecurityTokenSignatureKeyNotFoundException => "unknown_key",
            SecurityTokenInvalidSignatureException => "signature",
            SecurityTokenInvalidIssuerException => "issuer",
            _ => "invalid",
        };
}
