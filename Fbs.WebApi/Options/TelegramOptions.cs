namespace Fbs.WebApi.Options;

public class TelegramOptions
{
    public required string Token { get; set; }
    public required string WebhookUrl { get; set; }
}