using System.Text;

namespace Fbs.WebApi.Auth;

/// <summary>The body of a webhook from a sign-in provider, read exactly as it was sent, as that is what is signed.</summary>
public static class WebhookBody
{
    /// <summary>What Clerk and WorkOS send about an account is small, so anything larger isn't from them.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <returns>The body, or null if it is larger than <see cref="MaxBytes"/>.</returns>
    public static async Task<string?> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
