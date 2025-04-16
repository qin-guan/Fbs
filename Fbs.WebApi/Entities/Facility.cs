using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Entities;

public class Facility : Entity
{
    public string? Name { get; set; }

    public override string? GetId() => Name;
}