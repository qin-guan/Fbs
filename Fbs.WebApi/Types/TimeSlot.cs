using Fbs.WebApi.Entities;

namespace Fbs.WebApi.Types;

public record TimeSlot(DateTimeOffset StartDateTime, DateTimeOffset EndDateTime, BookingWithUser? Booking);