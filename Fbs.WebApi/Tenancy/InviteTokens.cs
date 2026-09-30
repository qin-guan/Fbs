using System.Security.Cryptography;
using System.Text;

namespace Fbs.WebApi.Tenancy;

public static class InviteTokens
{
    /// <summary>A token nobody could guess, 256 bits of it, in a form that is safe in an address.</summary>
    public static string Generate() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>What is kept of a token. It is random enough that a fast hash is enough, and it can't be turned back into the token.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
