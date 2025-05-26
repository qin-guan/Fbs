using Fbs.WebApi.Entities;

namespace Fbs.WebApi.Dtos;

public class BookingWithUser
{
    public Guid Id { get; set; }
    public string? FacilityName { get; set; }
    public string? Conduct { get; set; }
    public string? Description { get; set; }
    public string? PocName { get; set; }
    public string? PocPhone { get; set; }
    public DateTimeOffset? StartDateTime { get; set; }
    public DateTimeOffset? EndDateTime { get; set; }
    
    public User? User { get; set; }
}