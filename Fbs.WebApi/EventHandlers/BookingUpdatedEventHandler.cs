using System.Text.Encodings.Web;
using FastEndpoints;
using Fbs.WebApi.Events;
using Fbs.WebApi.Repository;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Fbs.WebApi.EventHandlers;

public class BookingUpdatedEventHandler(
    UserRepository userRepository,
    HtmlEncoder htmlEncoder,
    TelegramBotClient botClient
) : IEventHandler<BookingUpdatedEvent>
{
    private const string PurpleLightLyrics = """
                                             <i>
                                             Purple light
                                             In the valley
                                             That is where, I wanna be
                                             Infantry, best companions
                                             (With my rifle and my buddy and me)
                                             </i>
                                             """;

    public async Task HandleAsync(BookingUpdatedEvent booking, CancellationToken ct)
    {
        var users = await userRepository.GetListAsync(ct);
        var user = await userRepository.GetAsync(u => u.Phone == booking.UserPhone, ct);
        var subscribedUsers = users
            .Where(u => !string.IsNullOrWhiteSpace(u.TelegramChatId))
            .Where(u => u.Phone != booking.UserPhone)
            .Where(u =>
                (u.NotificationGroup == "All") ||
                (u.NotificationGroup == "Unit" && u.Unit == user.Unit)
            );

        await botClient.SendMessage(
            user.TelegramChatId!,
            $"""
             <b>UPDATED</b> booking for <b>{booking.FacilityName}</b>!
             
             <u>Conduct</u>
             {htmlEncoder.Encode(booking.Conduct ?? string.Empty)}

             <u>From</u>
             {booking.StartDateTime?.ToLocalTime():f}

             <u>To</u>
             {booking.EndDateTime?.ToLocalTime():f}

             <u>Point of contact</u>
             Name: {htmlEncoder.Encode(booking.PocName ?? string.Empty)}
             Contact: {htmlEncoder.Encode(booking.PocPhone ?? string.Empty)}

             <u>Booked by</u>
             Unit: {user.Unit}
             Name: {user.Name}
             Contact: {user.Phone}

             <u>Description</u>
             {(string.IsNullOrWhiteSpace(booking.Description) ? PurpleLightLyrics : htmlEncoder.Encode(booking.Description))}

             <u>Confirmation</u>
             {booking.Id}
             """,
            ParseMode.Html,
            cancellationToken: ct
        );

        await Parallel.ForEachAsync(subscribedUsers, ct, async (u, ct2) =>
        {
            await botClient.SendMessage(
                u.TelegramChatId!,
                $"""
                 <b>UPDATED</b> booking for <b>{booking.FacilityName}</b>!

                 <u>Conduct</u>
                 {htmlEncoder.Encode(booking.Conduct ?? string.Empty)}

                 <u>From</u>
                 {booking.StartDateTime?.ToLocalTime():f}

                 <u>To</u>
                 {booking.EndDateTime?.ToLocalTime():f}

                 <u>Point of contact</u>
                 Name: {htmlEncoder.Encode(booking.PocName ?? string.Empty)}
                 Contact: {htmlEncoder.Encode(booking.PocPhone ?? string.Empty)}

                 <u>Updated by</u>
                 Unit: {user.Unit}
                 Name: {user.Name}
                 Contact: {user.Phone}

                 <u>Description</u>
                 {(string.IsNullOrWhiteSpace(booking.Description) ? PurpleLightLyrics : htmlEncoder.Encode(booking.Description))}

                 <u>Confirmation</u>
                 {booking.Id}
                 """,
                ParseMode.Html,
                cancellationToken: ct2
            );
        });
    }
}