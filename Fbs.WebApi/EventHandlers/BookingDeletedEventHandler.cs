using System.Text.Encodings.Web;
using FastEndpoints;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Fbs.WebApi.EventHandlers;

public class BookingDeletedEventHandler(
    UserRepository userRepository,
    HtmlEncoder htmlEncoder,
    TelegramBotClient botClient
) : IEventHandler<BookingDeletedEvent>
{
    public async Task HandleAsync(BookingDeletedEvent booking, CancellationToken ct)
    {
        var users = await userRepository.GetListAsync(ct);
        var user = await userRepository.GetAsync(u => u.Phone == booking.UserPhone, ct);
        var cancelledBy =
            users.SingleOrDefault(u => u.Phone == booking.CancelledByPhone) ?? user;
        var subscribedUsers = users
            .Where(u => !string.IsNullOrWhiteSpace(u.TelegramChatId))
            .Where(u => u.Phone != booking.UserPhone)
            .Where(u =>
                (u.NotificationGroup == "All")
                || (u.NotificationGroup == "Unit" && u.Unit == user.Unit)
            );

        var description = string.IsNullOrWhiteSpace(booking.Description)
            ? string.Empty
            : $"\n{htmlEncoder.Encode(booking.Description)}";

        var message = $"""
            <b>CANCELLED</b> · <b>{booking.FacilityName}</b>
            {htmlEncoder.Encode(booking.Conduct ?? string.Empty)}
            {booking.StartDateTime?.ToLocalTime():f} – {booking.EndDateTime?.ToLocalTime():f}
            POC: {htmlEncoder.Encode(booking.PocName ?? string.Empty)}, {htmlEncoder.Encode(booking.PocPhone ?? string.Empty)}
            Cancelled by: {cancelledBy.Unit} / {cancelledBy.Name}, {cancelledBy.Phone}{description}
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
