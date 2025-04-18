using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Entities;

public class Booking : Entity<Guid>
{
    public DateTimeOffset? StartDateTime { get; set; }
    public DateTimeOffset? EndDateTime { get; set; }
    public string? Description { get; set; }

    public string? FacilityName { get; set; }
    public string? UserPhone { get; set; }
}