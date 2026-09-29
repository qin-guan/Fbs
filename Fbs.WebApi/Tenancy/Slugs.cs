using System.Text.RegularExpressions;

namespace Fbs.WebApi.Tenancy;

/// <summary>What an organisation can be called in an address.</summary>
public static partial class Slugs
{
    /// <summary>
    /// Words that mean something in the app's own addresses, or that people would take for the service itself, so
    /// nobody can make an organisation that looks like it is one of them.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "api", "app", "assets", "auth", "bot", "billing", "claim", "clerk", "dashboard", "docs", "fbs",
        "health", "help", "invite", "invites", "legal", "login", "logout", "new", "onboarding", "openapi", "privacy",
        "root", "scalar", "settings", "signin", "signup", "static", "status", "support", "system", "telegram", "tenant",
        "tenants", "terms", "webhooks", "www",
    };

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$")]
    private static partial Regex Pattern();

    /// <summary>Lower case letters, digits and hyphens, from 3 to 63 characters, starting and ending with a letter or digit.</summary>
    public static bool IsValid(string? slug) => slug is not null && Pattern().IsMatch(slug) && !slug.Contains("--", StringComparison.Ordinal);

    public static bool IsReserved(string slug) => Reserved.Contains(slug);
}
