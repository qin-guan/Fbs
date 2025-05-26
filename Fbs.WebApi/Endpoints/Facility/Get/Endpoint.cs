using FastEndpoints;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Facility.Get;

public class Endpoint(
    FacilityRepository facilityRepository
) : EndpointWithoutRequest<IEnumerable<Dtos.Facility>>
{
    public override void Configure()
    {
        Get("/Facility");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var facility = await facilityRepository.GetListAsync(ct);
        var dtos = facility.Select(f => new Dtos.Facility
        {
            Name = f.Name,
            Group = f.Group,
            Scope = f.Scope
        });
        await SendOkAsync(dtos, ct);
    }
}