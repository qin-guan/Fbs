using System.Security.Claims;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Repository;
using HotChocolate.Authorization;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Fbs.WebApi.Types;

public class Mutation
{
    [Authorize]
    public async Task<Booking> InsertBooking(
        string description,
        string facilityName,
        DateTimeOffset startDateTime,
        DateTimeOffset endDateTime,
        ClaimsPrincipal claimsPrincipal,
        BookingRepository bookingRepository,
        FacilityRepository facilityRepository,
        UserRepository userRepository,
        TelegramBotClient botClient,
        CancellationToken ct
    )
    {
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

        if (string.IsNullOrEmpty(description))
        {
            throw new Exception("A description must be specified.");
        }

        var bookings = await bookingRepository.GetListAsync(ct);
        var overlapping = bookings.Any(b => b.StartDateTime <= endDateTime && b.EndDateTime >= startDateTime);

        if (overlapping)
        {
            throw new Exception("Overlapping bookings.");
        }

        var facility = await facilityRepository.FindAsync(f => f.Name == facilityName, ct);
        if (facility is null)
        {
            throw new Exception("Facility does not exist.");
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
            Description = description,
            FacilityName = facilityName,
            UserPhone = phone
        };

        await bookingRepository.InsertAsync(booking, ct);
        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);

        await botClient.SendMessage(
            user.TelegramChatId!,
            $"""
             Your facility booking has been confirmed!

             <u>From</u>
             {startDateTime:f}

             <u>To</u>
             {endDateTime:f}

             <u>Booked by</u>
             Unit: {user.Unit}
             Contact: {user.Phone}

             Cancel or update your booking <a href="https://3sib-fbs.pages.dev/{booking.Id}">here</a>.
             """,
            ParseMode.Html,
            cancellationToken:
            ct
        );

        return booking;
    }
}