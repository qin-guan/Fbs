using FastEndpoints;
using Fbs.WebApi.Repository;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Fbs.WebApi.Endpoints.Bot.Post;

public class Endpoint(
    ILogger<Endpoint> logger,
    TelegramBotClient client,
    UserRepository userRepository
) : Endpoint<Update>
{
    public override void Configure()
    {
        Post("/Bot");
        Tags("Telegram");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Update req, CancellationToken ct)
    {
        switch (req.Message)
        {
            case { Contact: { } contact }:
            {
                var normalizedPhoneNumber = contact.PhoneNumber.StartsWith('+')
                    ? contact.PhoneNumber[1..]
                    : contact.PhoneNumber;

                if (normalizedPhoneNumber.Length is not 10)
                {
                    logger.LogWarning("Normalized phone number length is incorrect for {Phone}", normalizedPhoneNumber);
                }

                var user = await userRepository.FindAsync(u => u.Phone == normalizedPhoneNumber, ct);
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
                    replyMarkup: new[]
                    {
                        KeyboardButton.WithRequestContact("Link account")
                    },
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
}