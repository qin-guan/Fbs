using FastEndpoints;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.NominalRoll.Get;

public class Endpoint(NominalRollRepository nominalRoll): EndpointWithoutRequest<IEnumerable<Entities.NominalRoll>>
{
    public override void Configure()
    {
        Get("/NominalRoll");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(
            (await nominalRoll.GetListAsync(ct)).DistinctBy(n => n.Phone),
            ct
        );
    }
}