using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using SqlSugar;

namespace Fbs.WebApi.Tenancy;

/// <summary>
/// Finds the organisation in the address, and the member of it who is making the request, before anything else
/// in the endpoint runs. The endpoint is then only reached by an active member, and takes the organisation
/// from <see cref="ITenantContext"/> rather than from anything the caller sends.
/// </summary>
/// <remarks>
/// An endpoint for an organisation has <c>/t/{slug}</c> at the start of its route and adds this in
/// <c>Configure</c>, with <c>PreProcessor&lt;ResolveTenant&gt;()</c>. <see cref="RequireAdmin"/> goes after it
/// when only admins can use the endpoint.
/// <para>
/// Someone who isn't a member, or has left, is told there is no such organisation, the same as for one that
/// doesn't exist, so addresses can't be tried to find which do.
/// </para>
/// </remarks>
public sealed class ResolveTenant : IPreProcessor<object>
{
    public async Task PreProcessAsync(IPreProcessorContext<object> context, CancellationToken ct)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var response = http.Response;

        var account = await services.GetRequiredService<ICurrentAccount>().GetAsync(ct);
        if (account is null)
        {
            await response.SendUnauthorizedAsync(ct);
            return;
        }

        var sql = services.GetRequiredService<ISqlSugarClient>();
        var slug = http.Request.RouteValues["slug"] as string;
        var tenant = slug is null ? null : await sql.Queryable<Tenant>().FirstAsync(t => t.Slug == slug, ct);
        var accountId = account.Id;
        var member = tenant is null ? null : await FindMemberAsync(sql, tenant.Id, accountId, ct);

        if (tenant is null || member is null || member.Status == MemberStatus.Removed)
        {
            await response.SendNotFoundAsync(ct);
            return;
        }

        if (member.Status == MemberStatus.Pending)
        {
            await response.SendAsync(
                new { title = "Waiting for approval", status = 403, detail = "An admin has to let you in first.", code = "pending" },
                StatusCodes.Status403Forbidden,
                cancellation: ct
            );
            return;
        }

        if (member.Status != MemberStatus.Active || tenant.Status != TenantStatus.Active)
        {
            await response.SendAsync(
                new { title = "Not available", status = 403, detail = "This organisation is not available.", code = "unavailable" },
                StatusCodes.Status403Forbidden,
                cancellation: ct
            );
            return;
        }

        services.GetRequiredService<TenantContext>().Set(tenant, member, account);
    }

    private static Task<TenantMember?> FindMemberAsync(ISqlSugarClient sql, Guid tenantId, Guid accountId, CancellationToken ct) =>
        sql.Queryable<TenantMember>().FirstAsync(m => m.TenantId == tenantId && m.UserId == accountId, ct)!;
}
