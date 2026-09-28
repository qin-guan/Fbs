namespace Fbs.WebApi.Events;

public class BookingDeletedEvent
{
    public Guid Id { get; set; }
    public string? FacilityName { get; set; }
    public string? Conduct { get; set; }
    public string? Description { get; set; }
    public string? PocName { get; set; }
    public string? PocPhone { get; set; }
    public DateTimeOffset? StartDateTime { get; set; }
    public DateTimeOffset? EndDateTime { get; set; }
    public string? UserPhone { get; set; }

    /// <summary>
    /// Who cancelled the booking, which may be someone else in the booker's unit.
    /// </summary>
    public string? CancelledByPhone { get; set; }
}
