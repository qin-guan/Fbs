using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Entities;

public class Booking : Entity<Guid>
{
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string Description { get; set; }

    public Guid FacilityId { get; set; }
    public Guid UserId { get; set; }
}