namespace Fbs.WebApi.Endpoints.Claims.BySlug.Start.Post;

public class Response
{
    /// <summary>
    /// <c>https://t.me/&lt;bot&gt;?start=claim_&lt;token&gt;</c>. It has to be opened from the Telegram account the old version sent their
    /// login codes to.
    /// </summary>
    public required string Url { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
