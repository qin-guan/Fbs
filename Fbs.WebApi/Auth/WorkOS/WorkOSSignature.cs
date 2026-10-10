using System.Security.Cryptography;
using System.Text;

namespace Fbs.WebApi.Auth.WorkOS;

/// <summary>
/// Checks that a webhook came from WorkOS: each is signed, with a secret only WorkOS and we have, over the time it was sent and its
/// body, so neither can be changed, and one that is old is not accepted.
/// </summary>
public static class WorkOSSignature
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    /// <param name="secret">The webhook endpoint's secret, as WorkOS shows it.</param>
    /// <param name="header">The <c>WorkOS-Signature</c> header: <c>t=&lt;milliseconds since 1970&gt;, v1=&lt;hex&gt;</c>.</param>
    /// <param name="body">The body exactly as it was received.</param>
    public static bool IsValid(string secret, string? header, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.Split('=', 2))
            {
                case ["t", var value]:
                    timestamp = value;
                    break;
                case ["v1", var value]:
                    signatures.Add(value);
                    break;
            }
        }

        if (!long.TryParse(timestamp, out var milliseconds) || signatures.Count == 0)
        {
            return false;
        }

        // Too old or too new, which stops a webhook that was caught being sent again later
        DateTimeOffset sentAt;
        try
        {
            sentAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (now - sentAt is var age && (age > Tolerance || age < -Tolerance))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        foreach (var signature in signatures)
        {
            byte[] candidate;
            try
            {
                candidate = Convert.FromHexString(signature);
            }
            catch (FormatException)
            {
                // Not one of ours
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(expected, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
