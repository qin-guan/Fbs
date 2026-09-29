using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;

namespace Fbs.WebApi.Tenancy;

/// <summary>
/// Everything that belongs to one organisation lives under <c>/t/{slug}</c>. What is in the group can only
/// be reached by an active member of it, whose organisation and membership are found before the endpoint
/// runs, see <see cref="ResolveTenant"/>.
/// </summary>
public sealed class TenantGroup : Group
{
    public TenantGroup()
    {
        Configure(
            "t/{slug}",
            endpoint =>
            {
                endpoint.AuthSchemes(ClerkAuthentication.Scheme);
                endpoint.PreProcessor<ResolveTenant>(Order.Before);
            }
        );
    }
}

/// <summary>What only an admin of the organisation can reach: <see cref="TenantGroup"/>, and then that they are one.</summary>
public sealed class TenantAdminGroup : Group
{
    public TenantAdminGroup()
    {
        Configure(
            "t/{slug}",
            endpoint =>
            {
                endpoint.AuthSchemes(ClerkAuthentication.Scheme);
                endpoint.PreProcessor<ResolveTenant>(Order.Before);
                endpoint.PreProcessor<RequireAdmin>(Order.After);
            }
        );
    }
}
