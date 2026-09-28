namespace Fbs.WebApi.Dtos;

public class BookingSlot
{
    public string? FacilityName { get; set; }
    public DateTimeOffset StartDateTime { get; set; }
    public DateTimeOffset EndDateTime { get; set; }
}
