using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Members.Post;

/// <summary>
/// Adds someone by phone number who hasn't signed in, such as a person carried over from before. They belong to the
/// organisation from the moment they sign in and claim it, which is what an account still to be matched is for.
/// </summary>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : Endpoint<Request, MemberResponse>
{
    public override void Configure()
    {
        Post("/t/{slug}/Members");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        var phone = MemberPhones.Normalize(req.Phone, tenant.DefaultCountryCode);
        if (phone is null)
        {
            AddError(r => r.Phone, "That is not a phone number.", "phone-invalid");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (!await MemberChecks.UnitIsInAsync(sql, tenant.Id, req.UnitId, ct))
        {
            AddError(r => r.UnitId!, "That isn't one of this organisation's units.", "unit-unknown");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var member = new TenantMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            DisplayName = req.DisplayName.Trim(),
            Phone = phone,
            UnitId = req.UnitId,
            Role = req.Role,
            NotificationScope = req.NotificationScope,
            Status = MemberStatus.Unclaimed,
        };

        try
        {
            await sql.Insertable(member).ExecuteCommandAsync(ct);
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            AddError(r => r.Phone!, "Someone in this organisation has that number already.", "phone-taken");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.ResponseAsync(MemberResponse.From(member), StatusCodes.Status201Created, ct);
    }
}
