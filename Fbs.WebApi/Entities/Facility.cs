using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Entities;

public class Facility : Entity
{
    public string? Name { get; set; }
    public string? Group { get; set; }
    public List<string>? Scope { get; set; }

    public override string? GetId() => Name;
    public bool AvailableForAll => Scope?.Contains("All") ?? false;
}