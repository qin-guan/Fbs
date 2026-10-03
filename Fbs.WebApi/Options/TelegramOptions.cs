using System.Text.RegularExpressions;

namespace Fbs.WebApi.Options;

public partial class TelegramOptions
{
    public required string Token { get; set; }
    public required string WebhookUrl { get; set; }

    /// <summary>
    /// Sent to Telegram when registering the webhook and echoed back on every update in the
    /// <c>X-Telegram-Bot-Api-Secret-Token</c> header, so the bot endpoint can tell Telegram apart
    /// from anyone else who can reach it.
    /// </summary>
    public required string WebhookSecret { get; set; }

    /// <summary>
    /// The bot's username, without the @, that the link for connecting Telegram to an account opens. Asked of Telegram
    /// when it is first needed if left out.
    /// </summary>
    public string? BotUsername { get; set; }

    /// <summary>
    /// Telegram only accepts A-Z, a-z, 0-9, '_' and '-' in a secret token. At least 16 characters
    /// keeps it from being guessable.
    /// </summary>
    public static bool IsValidWebhookSecret(string? secret) =>
        secret is not null && WebhookSecretPattern().IsMatch(secret);

    [GeneratedRegex("^[A-Za-z0-9_-]{16,256}$")]
    private static partial Regex WebhookSecretPattern();
}
