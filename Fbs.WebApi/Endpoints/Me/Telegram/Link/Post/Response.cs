namespace Fbs.WebApi.Endpoints.Me.Telegram.Link.Post;

public class Response
{
    /// <summary>A link to open in Telegram, which starts the bot and tells it whose account to link the chat to.</summary>
    public required string Url { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
