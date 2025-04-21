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
    [Authorize]
    public async Task<Guid> DeleteBooking(
        ClaimsPrincipal claimsPrincipal,
        HtmlEncoder htmlEncoder,
        BookingRepository bookingRepository,
        UserRepository userRepository,
        TelegramBotClient botClient,
        Guid id,
        CancellationToken ct
    )
    {
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
        
        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
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
             Unit: {user.Unit}
             Name: {htmlEncoder.Encode(booking.PocName ?? string.Empty)}
             Contact: {htmlEncoder.Encode(booking.PocPhone ?? string.Empty)}

             <u>Cancelled by</u>
             Unit: {user.Unit}
             Name: {user.Name}
             Contact: {user.Phone}

             <u>Description</u>
             {htmlEncoder.Encode(booking.Description ?? string.Empty)}

             <u>Confirmation</u>
             {booking.Id}

             """,
            ParseMode.Html,
            cancellationToken:
            ct
        );

        return id;
    }

    [Authorize]
    public async Task<Booking> UpdateBooking(
        ClaimsPrincipal claimsPrincipal,
        HtmlEncoder htmlEncoder,
        BookingRepository bookingRepository,
        FacilityRepository facilityRepository,
        UserRepository userRepository,
        TelegramBotClient botClient,
        Guid id,
        string conduct,
        string description,
        string facilityName,
        string pocName,
        string pocPhone,
        CancellationToken ct
    )
    {
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

        var facility = await facilityRepository.FindAsync(f => f.Name == facilityName, ct);
        if (facility is null)
        {
            throw new Exception("Facility does not exist.");
        }

        booking.Conduct = conduct;
        booking.Description = description;
        booking.FacilityName = facilityName;
        booking.PocName = pocName;
        booking.PocPhone = pocPhone;
        booking.UserPhone = phone;

        await bookingRepository.UpdateAsync(booking, ct);
        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
        await botClient.SendMessage(
            user.TelegramChatId!,
            $"""
             Your booking for <b>{facilityName}</b> has been updated!

             <u>Conduct</u>
             {htmlEncoder.Encode(conduct)}

             <u>From</u>
             {booking.StartDateTime?.ToLocalTime():f}

             <u>To</u>
             {booking.EndDateTime?.ToLocalTime():f}

             <u>Point of contact</u>
             Unit: {user.Unit}
             Name: {htmlEncoder.Encode(pocName)}
             Contact: {htmlEncoder.Encode(pocPhone)}

             <u>Booked by</u>
             Unit: {user.Unit}
             Name: {user.Name}
             Contact: {user.Phone}

             <u>Description</u>
             {htmlEncoder.Encode(description)}

             <u>Confirmation</u>
             {booking.Id}

             Cancel or update your booking <a href="https://3sib-fbs.pages.dev/{booking.Id}">here</a>.
             """,
            ParseMode.Html,
            cancellationToken:
            ct
        );

        return booking;
    }

    [Authorize]
    public async Task<Booking> InsertBooking(
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

        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
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
             Unit: {user.Unit}
             Name: {htmlEncoder.Encode(pocName)}
             Contact: {htmlEncoder.Encode(pocPhone)}

             <u>Booked by</u>
             Unit: {user.Unit}
             Name: {user.Name}
             Contact: {user.Phone}

             <u>Description</u>
             {htmlEncoder.Encode(description)}

             <u>Confirmation</u>
             {booking.Id}

             Cancel or update your booking <a href="https://3sib-fbs.pages.dev/{booking.Id}">here</a>.
             """,
            ParseMode.Html,
            cancellationToken:
            ct
        );

        return booking;
    }
}