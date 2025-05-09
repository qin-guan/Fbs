using System.Security.Claims;
using System.Text.Encodings.Web;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;
using HotChocolate.Authorization;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Fbs.WebApi.Types;

public class Mutation
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

    [Authorize]
    public async Task<Guid> DeleteBooking(
        InstrumentationSource instrumentation,
        ClaimsPrincipal claimsPrincipal,
        HtmlEncoder htmlEncoder,
        BookingRepository bookingRepository,
        UserRepository userRepository,
        TelegramBotClient botClient,
        Guid id,
        CancellationToken ct
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var phone = claimsPrincipal.FindFirstValue("Phone");
        if (phone is null)
        {
            throw new Exception("User does not exist");
        }

        var booking = await bookingRepository.FindAsync(b => b.Id == id, ct);
        if (booking is null)
        {
            throw new Exception("Booking does not exist.");
        }

        if (booking.UserPhone != phone)
        {
            throw new Exception("You are not allowed to update this booking.");
        }

        await bookingRepository.DeleteAsync(b => b.Id == id, ct);

        var users = await userRepository.GetListAsync(ct);
        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
        var subscribedUsers = users
            .Where(u => u.Phone != phone)
            .Where(u =>
                (u.NotificationGroup == "All") ||
                (u.NotificationGroup == "Unit" && u.Unit == user.Unit)
            );

        await botClient.SendMessage(
            user.TelegramChatId!,
            $"""
             Your booking for <b>{booking.FacilityName}</b> has been cancelled!

             <u>Conduct</u>
             {htmlEncoder.Encode(booking.Conduct ?? string.Empty)}

             <u>From</u>
             {booking.StartDateTime?.ToLocalTime():f}

             <u>To</u>
             {booking.EndDateTime?.ToLocalTime():f}

             <u>Point of contact</u>
             Name: {htmlEncoder.Encode(booking.PocName ?? string.Empty)}
             Contact: {htmlEncoder.Encode(booking.PocPhone ?? string.Empty)}

             <u>Cancelled by</u>
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
                 A booking for <b>{booking.FacilityName}</b> has been cancelled!

                 <u>Conduct</u>
                 {htmlEncoder.Encode(booking.Conduct ?? string.Empty)}

                 <u>From</u>
                 {booking.StartDateTime?.ToLocalTime():f}

                 <u>To</u>
                 {booking.EndDateTime?.ToLocalTime():f}

                 <u>Point of contact</u>
                 Name: {htmlEncoder.Encode(booking.PocName ?? string.Empty)}
                 Contact: {htmlEncoder.Encode(booking.PocPhone ?? string.Empty)}

                 <u>Cancelled by</u>
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

        return id;
    }

    [Authorize]
    public async Task<Booking> UpdateBooking(
        InstrumentationSource instrumentation,
        ClaimsPrincipal claimsPrincipal,
        HtmlEncoder htmlEncoder,
        BookingRepository bookingRepository,
        FacilityRepository facilityRepository,
        UserRepository userRepository,
        TelegramBotClient botClient,
        Guid id,
        string conduct,
        string description,
        string pocName,
        string pocPhone,
        CancellationToken ct
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        var phone = claimsPrincipal.FindFirstValue("Phone");
        if (phone is null)
        {
            throw new Exception("User does not exist");
        }

        var booking = await bookingRepository.FindAsync(b => b.Id == id, ct);
        if (booking is null)
        {
            throw new Exception("Booking does not exist.");
        }

        if (booking.UserPhone != phone)
        {
            throw new Exception("You are not allowed to update this booking.");
        }

        if (string.IsNullOrEmpty(conduct))
        {
            throw new Exception("A conduct must be specified.");
        }

        if (conduct.Length > 100)
        {
            throw new Exception("Conduct must be less than 100 characters.");
        }

        booking.Conduct = conduct;
        booking.Description = description;
        booking.PocName = pocName;
        booking.PocPhone = pocPhone;
        booking.UserPhone = phone;

        await bookingRepository.UpdateAsync(booking, ct);

        var users = await userRepository.GetListAsync(ct);
        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
        var subscribedUsers = users
            .Where(u => u.Phone != phone)
            .Where(u =>
                (u.NotificationGroup == "All") ||
                (u.NotificationGroup == "Unit" && u.Unit == user.Unit)
            );

        await botClient.SendMessage(
            user.TelegramChatId!,
            $"""
             Your booking for <b>{booking.FacilityName}</b> has been updated!

             <u>Conduct</u>
             {htmlEncoder.Encode(conduct)}

             <u>From</u>
             {booking.StartDateTime?.ToLocalTime():f}

             <u>To</u>
             {booking.EndDateTime?.ToLocalTime():f}

             <u>Point of contact</u>
             Name: {htmlEncoder.Encode(pocName)}
             Contact: {htmlEncoder.Encode(pocPhone)}

             <u>Booked by</u>
             Unit: {user.Unit}
             Name: {user.Name}
             Contact: {user.Phone}

             <u>Description</u>
             {(string.IsNullOrWhiteSpace(booking.Description) ? PurpleLightLyrics : htmlEncoder.Encode(booking.Description))}

             <u>Confirmation</u>
             {booking.Id}

             Cancel or update your booking <a href="https://3sib-fbs.from.sg/booking/{booking.Id}">here</a>.
             """,
            ParseMode.Html,
            cancellationToken: ct
        );

        await Parallel.ForEachAsync(subscribedUsers, ct, async (u, ct2) =>
        {
            await botClient.SendMessage(
                u.TelegramChatId!,
                $"""
                 A booking for <b>{booking.FacilityName}</b> has been updated!

                 <u>Conduct</u>
                 {htmlEncoder.Encode(conduct)}

                 <u>From</u>
                 {booking.StartDateTime?.ToLocalTime():f}

                 <u>To</u>
                 {booking.EndDateTime?.ToLocalTime():f}

                 <u>Point of contact</u>
                 Name: {htmlEncoder.Encode(pocName)}
                 Contact: {htmlEncoder.Encode(pocPhone)}

                 <u>Booked by</u>
                 Unit: {user.Unit}
                 Name: {user.Name}
                 Contact: {user.Phone}

                 <u>Description</u>
                 {(string.IsNullOrWhiteSpace(booking.Description) ? PurpleLightLyrics : htmlEncoder.Encode(booking.Description))}

                 <u>Confirmation</u>
                 {booking.Id}

                 Cancel or update your booking <a href="https://3sib-fbs.from.sg/booking/{booking.Id}">here</a>.
                 """,
                ParseMode.Html,
                cancellationToken: ct2
            );
        });

        return booking;
    }

    [Authorize]
    public async Task<Booking> InsertBooking(
        InstrumentationSource instrumentation,
        ClaimsPrincipal claimsPrincipal,
        HtmlEncoder htmlEncoder,
        BookingRepository bookingRepository,
        FacilityRepository facilityRepository,
        UserRepository userRepository,
        TelegramBotClient botClient,
        string conduct,
        string description,
        string facilityName,
        string pocName,
        string pocPhone,
        DateTimeOffset startDateTime,
        DateTimeOffset endDateTime,
        CancellationToken ct
    )
    {
        using var activity = instrumentation.ActivitySource.StartActivity();

        if (DateTimeOffset.Now >= startDateTime)
        {
            throw new Exception("Start time must be in the future.");
        }

        if (startDateTime >= endDateTime)
        {
            throw new Exception("End time must be after start time.");
        }

        if (startDateTime.Minute % 15 != 0 || (endDateTime - startDateTime).TotalMinutes % 15 != 0)
        {
            throw new Exception("Timespan of booking must be in 15 minute intervals.");
        }

        if (string.IsNullOrEmpty(conduct))
        {
            throw new Exception("A conduct must be specified.");
        }

        if (conduct.Length > 100)
        {
            throw new Exception("Conduct must be less than 100 characters.");
        }

        var facility = await facilityRepository.FindAsync(f => f.Name == facilityName, ct);
        if (facility is null)
        {
            throw new Exception("Facility does not exist.");
        }

        var bookings = await bookingRepository.GetListAsync(ct);
        var overlapping = bookings.Any(b =>
            b.FacilityName == facilityName &&
            b.StartDateTime < endDateTime &&
            b.EndDateTime > startDateTime
        );

        if (overlapping)
        {
            throw new Exception("Overlapping bookings.");
        }

        var phone = claimsPrincipal.FindFirstValue("Phone");
        if (phone is null)
        {
            throw new Exception("User does not exist");
        }

        var booking = new Booking
        {
            StartDateTime = startDateTime,
            EndDateTime = endDateTime,
            Conduct = conduct,
            Description = description,
            FacilityName = facilityName,
            PocName = pocName,
            PocPhone = pocPhone,
            UserPhone = phone
        };

        await bookingRepository.InsertAsync(booking, ct);

        var users = await userRepository.GetListAsync(ct);
        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
        var subscribedUsers = users
            .Where(u => u.Phone != phone)
            .Where(u =>
                (u.NotificationGroup == "All") ||
                (u.NotificationGroup == "Unit" && u.Unit == user.Unit)
            );

        await botClient.SendMessage(
            user.TelegramChatId!,
            $"""
             Your booking for <b>{facilityName}</b> has been created!

             <u>Conduct</u>
             {htmlEncoder.Encode(conduct)}

             <u>From</u>
             {startDateTime.ToLocalTime():f}

             <u>To</u>
             {endDateTime.ToLocalTime():f}

             <u>Point of contact</u>
             Name: {htmlEncoder.Encode(pocName)}
             Contact: {htmlEncoder.Encode(pocPhone)}

             <u>Booked by</u>
             Unit: {user.Unit}
             Name: {user.Name}
             Contact: {user.Phone}

             <u>Description</u>
             {(string.IsNullOrWhiteSpace(booking.Description) ? PurpleLightLyrics : htmlEncoder.Encode(booking.Description))}

             <u>Confirmation</u>
             {booking.Id}

             Cancel or update your booking <a href="https://3sib-fbs.from.sg/booking/{booking.Id}">here</a>.
             """,
            ParseMode.Html,
            cancellationToken: ct
        );

        await Parallel.ForEachAsync(subscribedUsers, ct, async (u, ct2) =>
        {
            await botClient.SendMessage(
                u.TelegramChatId!,
                $"""
                 A booking for <b>{facilityName}</b> has been created!

                 <u>Conduct</u>
                 {htmlEncoder.Encode(conduct)}

                 <u>From</u>
                 {startDateTime.ToLocalTime():f}

                 <u>To</u>
                 {endDateTime.ToLocalTime():f}

                 <u>Point of contact</u>
                 Name: {htmlEncoder.Encode(pocName)}
                 Contact: {htmlEncoder.Encode(pocPhone)}

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
                cancellationToken: ct2
            );
        });

        return booking;
    }
}