using FastEndpoints;
using Fbs.WebApi.RateLimiting;
using Fbs.WebApi.Telemetry;
using Microsoft.AspNetCore.RateLimiting;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Tenants.Post;

/// <summary>Anyone signed in can make an organisation, and becomes its admin.</summary>
[RequiresClerk]
public class Endpoint(ICurrentAccount currentAccount, ISqlSugarClient sql, IOptions<TenantLimits> limits, AuditLog audit) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/Tenants");
        AuthSchemes(ClerkAuthentication.Scheme);
        Options(x => x.RequireRateLimiting(RateLimitPolicies.CreateOrganization));
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var account = await currentAccount.GetAsync(ct);
        if (account is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var slug = req.Slug;
        if (Slugs.IsReserved(slug))
        {
            AddError(r => r.Slug, "That address can't be used.", "slug-reserved");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var accountId = account.Id;
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = req.Name.Trim(),
            TimeZone = string.IsNullOrWhiteSpace(req.TimeZone) ? "UTC" : req.TimeZone.Trim(),
            DefaultCountryCode = string.IsNullOrWhiteSpace(req.DefaultCountryCode) ? "65" : req.DefaultCountryCode.Trim(),
            CreatedByUserId = accountId,
        };
        var admin = new TenantMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = accountId,
            DisplayName = account.Name ?? account.Email ?? "Admin",
            Role = MemberRole.Admin,
            Status = MemberStatus.Active,
        };

        try
        {
            using var tran = sql.Ado.UseTran();

            // Counting the tenants they have made is only right if nobody else is making one for them at the
            // same moment, so the account is locked first, and what they have is read with a locking read, as a
            // plain one would see how things were when the transaction began
            await sql.Queryable<UserAccount>().Where(a => a.Id == accountId).TranLock(DbLockType.Wait).FirstAsync(ct);
            var made = (await sql.Queryable<Tenant>().Where(t => t.CreatedByUserId == accountId).Select(t => t.Id).TranLock(DbLockType.Wait).ToListAsync(ct)).Count;
            if (made >= limits.Value.MaxTenantsPerUser)
            {
                AddError($"You can make up to {limits.Value.MaxTenantsPerUser} organizations.", "tenant-limit");
                await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
                return;
            }

            await sql.Insertable(tenant).ExecuteCommandAsync(ct);
            await sql.Insertable(admin).ExecuteCommandAsync(ct);
            await audit.WriteAsync(tenant.Id, admin.Id, "tenant.created", "Made the organisation.", "tenant", tenant.Id, ct);
            tran.CommitTran();
            FbsMetrics.TenantsCreated.Add(1);
        }
        catch (Exception e) when (e.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            AddError(r => r.Slug, "That address is taken.", "slug-taken");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.ResponseAsync(new Response { Slug = tenant.Slug, Name = tenant.Name, TimeZone = tenant.TimeZone }, StatusCodes.Status201Created, ct);
    }
}
