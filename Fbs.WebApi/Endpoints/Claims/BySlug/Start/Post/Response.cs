namespace Fbs.WebApi.Endpoints.Claims.BySlug.Start.Post;

public class Response
{
    /// <summary>A link to open in the Telegram chat they were linked to, which tells the bot to give them their place.</summary>
    public required string Url { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
