using FastEndpoints;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Services.GoogleSheets;

namespace Fbs.WebApi.Endpoints.Facilities.Get;

public class Endpoint(GoogleSheetsFacilitiesService facilitiesService): EndpointWithoutRequest<List<Facility>>
{
    public override void Configure()
    {
        Get("/Facilities");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendOkAsync(await facilitiesService.GetFacilitiesAsync(), ct);
    }
}
