using System.Text.Encodings.Web;
using FastEndpoints;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Fbs.WebApi.EventHandlers;

public class BookingUpdatedEventHandler(
    IUserRepository userRepository,
    HtmlEncoder htmlEncoder,
    TelegramBotClient botClient
) : IEventHandler<BookingUpdatedEvent>
{
    public async Task HandleAsync(BookingUpdatedEvent booking, CancellationToken ct)
    {
        var users = await userRepository.GetListAsync(ct);
        var user = await userRepository.GetAsync(u => u.Phone == booking.UserPhone, ct);
        var subscribedUsers = users
            .Where(u => !string.IsNullOrWhiteSpace(u.TelegramChatId))
            .Where(u => u.Phone != booking.UserPhone)
            .Where(u =>
                (u.NotificationGroup == "All")
                || (u.NotificationGroup == "Unit" && u.Unit == user.Unit)
            );

        // Point out when the time slot moved, so nobody turns up at the old time
        var previously =
            booking.PreviousStartDateTime is null || booking.PreviousEndDateTime is null
                ? string.Empty
                : $"\nWas: {booking.PreviousStartDateTime?.ToLocalTime():f} – {booking.PreviousEndDateTime?.ToLocalTime():f}";

        var description = string.IsNullOrWhiteSpace(booking.Description)
            ? string.Empty
            : $"\n{htmlEncoder.Encode(booking.Description)}";

        var message = $"""
            <b>UPDATED</b> · <b>{booking.FacilityName}</b>
            {htmlEncoder.Encode(booking.Conduct ?? string.Empty)}
            {booking.StartDateTime?.ToLocalTime():f} – {booking.EndDateTime?.ToLocalTime():f}{previously}
            POC: {htmlEncoder.Encode(booking.PocName ?? string.Empty)}, {htmlEncoder.Encode(booking.PocPhone ?? string.Empty)}
            Updated by: {user.Unit} / {user.Name}, {user.Phone}{description}
            Ref: {booking.Id}
            """;

        await botClient.SendMessage(
            user.TelegramChatId!,
            message,
            ParseMode.Html,
            cancellationToken: ct
        );

        await Parallel.ForEachAsync(
            subscribedUsers,
            ct,
            async (u, ct2) =>
            {
                await botClient.SendMessage(
                    u.TelegramChatId!,
                    message,
                    ParseMode.Html,
                    cancellationToken: ct2
                );
            }
        );
    }
}
