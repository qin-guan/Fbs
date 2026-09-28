using Fbs.WebApi.Dtos;

namespace Fbs.WebApi.Endpoints.Booking.Batch.Post;

public class Request
{
    public string? Conduct { get; set; }
    public string? Description { get; set; }
    public string? PocName { get; set; }
    public string? PocPhone { get; set; }
    public List<BookingSlot> Slots { get; set; } = [];
}
