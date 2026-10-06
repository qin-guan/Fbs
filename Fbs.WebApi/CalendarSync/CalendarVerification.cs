using System.Security.Cryptography;
using System.Text;

namespace Fbs.WebApi.CalendarSync;

/// <summary>
/// The code an admin reads out of their calendar to show they can see it. The code is written on the event,
/// and only a hash of it is kept here.
/// </summary>
public static class CalendarVerification
{
    /// <summary>How the event is named: this, then the code, so it can be read off the calendar.</summary>
    public const string SummaryPrefix = "Fbs ";

    /// <summary>How long the code is accepted for.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    // No 0, 1, i, l or o, so a code read off a calendar is not mistaken for another
    private const string Alphabet = "23456789abcdefghjkmnpqrstuvwxyz";

    /// <summary>The same event is reused for every attempt, and the id is one Google Calendar allows.</summary>
    public static string EventId(Guid tenantId) => "fbscheck" + tenantId.ToString("N");

    public static string NewCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        return new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
    }

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)))).ToLowerInvariant();

    public static bool Matches(string? storedHash, string code)
    {
        if (storedHash is not { Length: 64 })
        {
            return false;
        }

        byte[] stored;
        try
        {
            stored = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)));
        return CryptographicOperations.FixedTimeEquals(stored, actual);
    }

    public static string Normalize(string code) => code.Trim().ToLowerInvariant();
}