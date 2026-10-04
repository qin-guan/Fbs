using System.Security.Cryptography;
using System.Text;

namespace Fbs.WebApi.Auth.Clerk;

/// <summary>
/// Checks that a webhook came from Clerk, which sends them with Svix: each is signed, with a secret only Clerk and we have, over
/// its ID, the time it was sent and its body, so none of those can be changed, and one that is old is not accepted.
/// </summary>
public static class SvixSignature
{
    private const string SecretPrefix = "whsec_";

    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    /// <param name="secret">The signing secret, <c>whsec_</c> and then base64.</param>
    /// <param name="id">The <c>svix-id</c> header.</param>
    /// <param name="timestamp">The <c>svix-timestamp</c> header: seconds since 1970.</param>
    /// <param name="signatures">The <c>svix-signature</c> header: <c>v1,&lt;base64&gt;</c>, and more of them, separated by spaces, while the secret is being changed.</param>
    /// <param name="body">The body exactly as it was received.</param>
    public static bool IsValid(string secret, string? id, string? timestamp, string? signatures, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(signatures) || !long.TryParse(timestamp, out var seconds))
        {
            return false;
        }

        // Too old or too new, which stops a webhook that was caught being sent again later
        if (now - DateTimeOffset.FromUnixTimeSeconds(seconds) is var age && (age > Tolerance || age < -Tolerance))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? secret[SecretPrefix.Length..] : secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
        foreach (var candidate in signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = candidate.Split(',', 2);
            if (parts is not ["v1", var signature])
            {
                continue;
            }

            try
            {
                if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(signature)))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // Not one of ours
            }
        }

        return false;
    }
}
