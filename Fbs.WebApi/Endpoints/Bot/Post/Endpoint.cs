using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Fbs.WebApi.Claims;
using Fbs.WebApi.Options;
using Fbs.WebApi.Repository;
using Fbs.WebApi.TelegramLinks;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Fbs.WebApi.Endpoints.Bot.Post;

public class Endpoint(
    ILogger<Endpoint> logger,
    TelegramBotClient client,
    IUserRepository userRepository,
    IOptions<TelegramOptions> options
) : Endpoint<Update>
{
    private const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

    public override void Configure()
    {
        Post("/Bot");
        Tags("Telegram");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Update req, CancellationToken ct)
    {
        // Anyone can reach this endpoint, so only accept updates that carry the secret Telegram was
        // given when the webhook was registered
        if (!HasValidSecretToken())
        {
            logger.LogWarning("Rejected a Telegram update without a valid secret token");
            await Send.UnauthorizedAsync(ct);
            return;
        }

        switch (req.Message)
        {
            // Linking a phone number to a chat lets that chat receive the number's login codes, so
            // it must be the sender's own number, shared from their own private chat. A contact can
            // be forwarded or made up, which is why the bot doesn't trust the phone number alone.
            case { Contact: { } contact } when !SharedOwnContact(req.Message, contact):
            {
                logger.LogWarning("Rejected a Telegram contact that isn't the sender's own");

                await client.SendMessage(
                    req.Message.Chat,
                    "Please share your own phone number using the button below.",
                    replyMarkup: new[] { KeyboardButton.WithRequestContact("Link account") },
                    cancellationToken: ct
                );

                break;
            }
            case { Contact: { } contact }:
            {
                var normalizedPhoneNumber = contact.PhoneNumber.StartsWith('+')
                    ? contact.PhoneNumber[1..]
                    : contact.PhoneNumber;

                if (normalizedPhoneNumber.Length is not 10)
                {
                    logger.LogWarning(
                        "Normalized phone number length is incorrect for {Phone}",
                        normalizedPhoneNumber
                    );
                }

                var user = await userRepository.FindAsync(
                    u => u.Phone == normalizedPhoneNumber,
                    ct
                );
                if (user is null)
                {
                    await client.SendMessage(
                        req.Message.Chat,
                        """
                        Thank you!

                        Your number has not been whitelisted. Please approach your unit S3 for whitelisting.
                        """,
                        replyMarkup: new ReplyKeyboardRemove(),
                        cancellationToken: ct
                    );

                    break;
                }

                user.TelegramChatId = req.Message.Chat.Id.ToString();

                await userRepository.UpdateAsync(user, ct);

                await client.SendMessage(
                    req.Message.Chat,
                    $"""
                    You've been successfully registered as {user.Name}!

                    You may now use Telegram to authenticate with the Facility Booking System.

                    Make a booking <a href="https://3sib-fbs.from.sg">here</a>.
                    """,
                    ParseMode.Html,
                    replyMarkup: new ReplyKeyboardRemove(),
                    cancellationToken: ct
                );

                break;
            }
            case { Text: "/start" }:
            {
                await client.SendMessage(
                    req.Message.Chat,
                    """
                    <b>Welcome to 3SIB Facility Booking System</b>

                    Please link your Telegram account clicking the button below =)
                    """,
                    ParseMode.Html,
                    replyMarkup: new[] { KeyboardButton.WithRequestContact("Link account") },
                    cancellationToken: ct
                );

                break;
            }
            // Opened from the link the app makes for claiming a place carried over from before
            case { Text: { } claimText }
                when claimText.StartsWith("/start " + MemberClaims.StartPrefix, StringComparison.Ordinal)
                    && req.Message.Chat.Type == ChatType.Private
                    && HttpContext.RequestServices.GetService<MemberClaims>() is not null:
            {
                var claims = HttpContext.RequestServices.GetRequiredService<MemberClaims>();
                var token = claimText["/start ".Length..].Trim()[MemberClaims.StartPrefix.Length..];
                var result = await claims.CompleteAsync(token, req.Message.Chat.Id.ToString(), ct);
                await client.SendMessage(
                    req.Message.Chat,
                    result.Outcome switch
                    {
                        ClaimOutcome.Claimed => $"Welcome back, {result.MemberName}! You are in {result.OrganizationName} now, so you can sign in with your account.",
                        ClaimOutcome.NoPlaceForChat => $"This chat is not linked to anyone in {result.OrganizationName}. Open the link from the Telegram account you used before, or ask an admin to add you.",
                        ClaimOutcome.AlreadyMember => "You are in that organization already.",
                        ClaimOutcome.Unavailable => "Claiming a place is not available for that organization.",
                        _ => "That link has expired or has been used already. Make a new one in the app.",
                    },
                    cancellationToken: ct
                );

                break;
            }
            // Opened from the link the app makes for connecting an account: the bot is told which account by the token
            case { Text: { } text }
                when text.StartsWith("/start ", StringComparison.Ordinal)
                    && req.Message.Chat.Type == ChatType.Private
                    && HttpContext.RequestServices.GetService<TelegramLinker>() is not null:
            {
                var linker = HttpContext.RequestServices.GetRequiredService<TelegramLinker>();
                var name = await linker.CompleteAsync(text["/start ".Length..].Trim(), req.Message.Chat.Id.ToString(), ct);
                await client.SendMessage(
                    req.Message.Chat,
                    name is null
                        ? "That link has expired or has been used already. Make a new one in the app."
                        : $"Connected to {name}. You will be told about bookings here.",
                    cancellationToken: ct
                );

                break;
            }
            case not null:
            {
                await client.SendMessage(
                    req.Message.Chat,
                    "Unknown command :(",
                    cancellationToken: ct
                );

                break;
            }
            default:
            {
                logger.LogInformation("Received unhandled update");
                break;
            }
        }

        await Send.OkAsync();
    }

    private bool HasValidSecretToken()
    {
        var received = HttpContext.Request.Headers[SecretTokenHeader].ToString();
        var expected = options.Value.WebhookSecret;

        // Compared in constant time, so the secret can't be guessed a character at a time
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(received),
            Encoding.UTF8.GetBytes(expected)
        );
    }

    private static bool SharedOwnContact(Message message, Contact contact)
    {
        return message.Chat.Type == ChatType.Private
            && message.From is { } sender
            && contact.UserId == sender.Id;
    }
}
