using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.RateLimiting;

/// <summary>What is limited, and for whom. Each is counted separately, so using one doesn't use up another.</summary>
public static class RateLimitPolicies
{
    /// <summary>Making an organisation: a person can only make a few anyway, and this stops trying to fill the database faster than that.</summary>
    public const string CreateOrganization = "create-organization";

    /// <summary>Looking at and using a link to join, or to claim a place: the links can't be guessed, and this stops trying.</summary>
    public const string Join = "join";

    /// <summary>Making links to connect Telegram, each of which is stored until it runs out.</summary>
    public const string LinkTelegram = "link-telegram";

    /// <summary>Webhooks, which anyone can send to, so each address gets so many.</summary>
    public const string Webhook = "webhook";

    /// <summary>Downloading a copy of somebody's, or an organisation's, data, which takes a lot of reading.</summary>
    public const string Export = "export";

    public static readonly IReadOnlyDictionary<string, PolicyLimit> Defaults = new Dictionary<string, PolicyLimit>
    {
        [CreateOrganization] = new() { PermitLimit = 10, WindowSeconds = 3600 },
        [Join] = new() { PermitLimit = 30, WindowSeconds = 600 },
        [LinkTelegram] = new() { PermitLimit = 20, WindowSeconds = 600 },
        [Webhook] = new() { PermitLimit = 120, WindowSeconds = 60 },
        [Export] = new() { PermitLimit = 5, WindowSeconds = 600 },
    };
}

public sealed class PolicyLimit
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}

public sealed class RateLimitOptions
{
    /// <summary>Everything is allowed when this is off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Overrides of <see cref="RateLimitPolicies.Defaults"/>, by the name of the policy.</summary>
    public Dictionary<string, PolicyLimit> Limits { get; set; } = [];
}

public static class RateLimitingExtensions
{
    /// <summary>The address ranges that the proxy in front of the API can be at, which is the only place forwarded headers are believed from.</summary>
    public static readonly string[] PrivateNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "fc00::/7", "::1/128"];

    /// <summary>
    /// Believes <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> only from a proxy in a private range, such as Coolify's,
    /// and from nowhere else, so somebody reaching the API directly can't say they are somebody else. With the
    /// proxy there, the address that is used is the last one in the header, which is the one the proxy saw.
    /// </summary>
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>() is { Length: > 0 } configured ? configured : PrivateNetworks;
        var limit = configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = limit;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }

    public static IServiceCollection AddFbsRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(options =>
        {
            configuration.GetSection("RateLimits").Bind(options);
        });

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    """{"title":"Too many requests","status":429,"detail":"Wait a little and try again.","code":"rate-limited"}""",
                    token
                );
            };

            // Read when the first request is made rather than here, so that what is configured for a test is what is used
            foreach (var (name, defaults) in RateLimitPolicies.Defaults)
            {
                var byIpAddress = name == RateLimitPolicies.Webhook;
                limiter.AddPolicy(
                    name,
                    context =>
                    {
                        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                        if (!options.Enabled)
                        {
                            return RateLimitPartition.GetNoLimiter("off");
                        }

                        var limit = options.Limits.GetValueOrDefault(name) ?? defaults;
                        return RateLimitPartition.GetFixedWindowLimiter(
                            byIpAddress ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown" : PersonOf(context),
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = limit.PermitLimit > 0 ? limit.PermitLimit : defaults.PermitLimit,
                                Window = TimeSpan.FromSeconds(limit.WindowSeconds > 0 ? limit.WindowSeconds : defaults.WindowSeconds),
                                QueueLimit = 0,
                            }
                        );
                    }
                );
            }
        });

        return services;
    }

    /// <summary>
    /// Who is asking: their account, from their session token, or failing that the address they are at. Everyone at one
    /// address is not the same person, as a whole unit can share one.
    /// </summary>
    private static string PersonOf(HttpContext context) =>
        context.User.FindFirst("sub")?.Value is { Length: > 0 } sub ? $"user:{sub}" : $"ip:{context.Connection.RemoteIpAddress}";
}
