using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Endpoints.Facility.Get;

public class Endpoint(
    FacilityRepository facilityRepository,
    UserRepository userRepository
) : EndpointWithoutRequest<IEnumerable<Dtos.Facility>>
{
    public override void Configure()
    {
        Get("/Facility");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var phone = User.ClaimValue("Phone");
        
        var user = await userRepository.GetAsync(u => u.Phone == phone, ct);
        if (user.Unit is null)
        {
            throw new Exception("User needs to have a unit in order to retrieve facility list.");
        }
        
        var facilities= await facilityRepository.GetListAsync(ct);

        var eligible = facilities
            .Where(f => f.Scope is not null)
            .Where(f => f.AvailableForAll || f.Scope!.Contains(user.Unit));
        
        var dtos = eligible.Select(f => new Dtos.Facility
        {
            Name = f.Name,
            Group = f.Group,
            Scope = f.Scope
        });
        
        await SendOkAsync(dtos, ct);
    }
}