namespace Fbs.WebApi.Dtos;

public class TimeSlot
{
    public DateTimeOffset StartDateTime { get; set; }
    public DateTimeOffset EndDateTime { get; set; }
    public BookingWithUser? Booking { get; set; }
}