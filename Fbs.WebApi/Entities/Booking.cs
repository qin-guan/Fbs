using Fbs.WebApi.Repository;
using FreeSql.DataAnnotations;
using MemoryPack;

namespace Fbs.WebApi.Entities;

[MemoryPackable]
[Table(Name = "Bookings")]
public partial class Booking : Entity<Guid>
{
    [Column(IsPrimary = true)]
    [MemoryPackOrder(0)]
    public override Guid Id { get; set; }

    [Column(DbType = "TEXT")]
    public DateTimeOffset? StartDateTime { get; set; }

    [Column(DbType = "TEXT")]
    public DateTimeOffset? EndDateTime { get; set; }

    [Column(StringLength = 100)]
    public string? Conduct { get; set; }

    [Column(StringLength = 500)]
    public string? Description { get; set; }

    [Column(StringLength = 100)]
    public string? PocName { get; set; }

    [Column(StringLength = 50)]
    public string? PocPhone { get; set; }

    [Column(StringLength = 100)]
    public string? FacilityName { get; set; }

    [Column(StringLength = 50)]
    public string? UserPhone { get; set; }
}