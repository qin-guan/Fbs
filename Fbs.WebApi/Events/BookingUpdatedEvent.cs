namespace Fbs.WebApi.Events;

public class BookingUpdatedEvent
{
    public Guid Id { get; set; }
    public string? FacilityName { get; set; }
    public string? Conduct { get; set; }
    public string? Description { get; set; }
    public string? PocName { get; set; }
    public string? PocPhone { get; set; }
    public DateTimeOffset? StartDateTime { get; set; }
    public DateTimeOffset? EndDateTime { get; set; }

    /// <summary>The start before this update, if the time slot changed.</summary>
    public DateTimeOffset? PreviousStartDateTime { get; set; }

    /// <summary>The end before this update, if the time slot changed.</summary>
    public DateTimeOffset? PreviousEndDateTime { get; set; }

    public string? UserPhone { get; set; }
}
