using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Endpoints.Org.Units;

public class UnitResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public static UnitResponse From(DataUnit unit) => new() { Id = unit.Id, Name = unit.Name };
}
