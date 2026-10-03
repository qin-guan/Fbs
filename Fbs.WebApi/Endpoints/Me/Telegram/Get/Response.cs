namespace Fbs.WebApi.Endpoints.Me.Telegram.Get;

public class Response
{
    public required bool Linked { get; init; }

    public DateTimeOffset? LinkedAt { get; init; }
}
