using Fbs.WebApi.Repository;
using MemoryPack;

namespace Fbs.WebApi.Entities;

[MemoryPackable]
public partial class Booking : Entity<Guid>
{
    public DateTimeOffset? StartDateTime { get; set; }
    public DateTimeOffset? EndDateTime { get; set; }
    public string? Conduct { get; set; }
    public string? Description { get; set; }
    public string? PocName { get; set; }
    public string? PocPhone { get; set; }

    public string? FacilityName { get; set; }
    public string? UserPhone { get; set; }
}