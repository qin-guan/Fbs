using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Entities;

public class NominalRoll : Entity
{
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public string? Phone { get; set; }

    public override string? GetId() => Phone;
}