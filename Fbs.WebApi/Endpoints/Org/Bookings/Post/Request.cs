namespace Fbs.WebApi.Endpoints.Org.Bookings.Post;

public class Request
{
    public string? Conduct { get; set; }

    public string? Description { get; set; }

    /// <summary>Who to contact about the booking, as it is to be written on it.</summary>
    public string? PocName { get; set; }

    public string? PocPhone { get; set; }

    /// <summary>What to book. More than one is booked together: all of them, or none if any can't be.</summary>
    public List<Slot> Slots { get; set; } = [];
}

public class Slot
{
    public Guid FacilityId { get; set; }

    public DateTimeOffset StartDateTime { get; set; }

    public DateTimeOffset EndDateTime { get; set; }
}
