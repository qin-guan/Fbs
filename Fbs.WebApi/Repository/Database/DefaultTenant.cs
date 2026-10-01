using Fbs.WebApi.Data.Entities;
using SqlSugar;

namespace Fbs.WebApi.Repository.Database;

/// <summary>
/// The one tenant the API serves until tenants are told apart by the request, set by
/// <c>Storage:TenantSlug</c>.
/// </summary>
public sealed class DefaultTenant(ISqlSugarClient sql, IConfiguration configuration)
{
    private Guid? _id;

    public string Slug => configuration["Storage:TenantSlug"] ?? "3sib";

    public async Task<Guid> GetIdAsync(CancellationToken cancellationToken = default)
    {
        if (_id is { } id)
        {
            return id;
        }

        var slug = Slug;
        var tenant = await sql.Queryable<Tenant>().FirstAsync(t => t.Slug == slug, cancellationToken);
        if (tenant is null)
        {
            throw new InvalidOperationException(
                $"There is no tenant with the slug '{slug}'. Import the data first, or set Storage:TenantSlug."
            );
        }

        _id = tenant.Id;
        return tenant.Id;
    }
}
