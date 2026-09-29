using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Members.ById.Put;

/// <summary>
/// Changes a member: who they are, their unit, whether they are an admin, whose bookings they hear about, and whether they
/// are let in or removed, which is also how someone waiting for approval is approved or turned away.
/// </summary>
/// <remarks>The organisation always keeps an admin: the change that would take the last one is refused.</remarks>
[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : Endpoint<Request, MemberResponse>
{
    public override void Configure()
    {
        Put("/t/{slug}/Members/{id}");
        AuthSchemes(ClerkAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        var tenantId = tenant.Id;
        var id = req.Id;

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(req.Phone))
        {
            phone = MemberPhones.Normalize(req.Phone, tenant.DefaultCountryCode);
            if (phone is null)
            {
                AddError(r => r.Phone!, "That is not a phone number.", "phone-invalid");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }
        }

        if (!await MemberChecks.UnitIsInAsync(sql, tenantId, req.UnitId, ct))
        {
            AddError(r => r.UnitId!, "That isn't one of this organisation's units.", "unit-unknown");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        try
        {
            using var tran = sql.Ado.UseTran();

            // The admins are locked before anything is decided from them, or two admins making each other members
            // at the same moment would each see the other still there
            var admins = await MemberChecks.LockActiveAdminsAsync(sql, tenantId, ct);
            var member = await sql.Queryable<TenantMember>().Where(m => m.Id == id && m.TenantId == tenantId).TranLock(DbLockType.Wait).FirstAsync(ct);
            if (member is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var status = req.Membership == MembershipState.Removed ? MemberStatus.Removed : member.UserId is null ? MemberStatus.Unclaimed : MemberStatus.Active;
            if (status == MemberStatus.Unclaimed && phone is null)
            {
                AddError(r => r.Phone!, "Someone who hasn't signed in yet is found by their phone number, so it can't be left out.", "phone-needed");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            var staysAnAdmin = status == MemberStatus.Active && req.Role == MemberRole.Admin;
            if (admins.Any(a => a.Id == id) && !staysAnAdmin && admins.Count == 1)
            {
                AddError("The organisation needs an admin, so the last one can't be made a member or removed.", "last-admin");
                await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
                return;
            }

            var displayName = req.DisplayName.Trim();
            var unitId = req.UnitId;
            var role = req.Role;
            var scope = req.NotificationScope;
            await sql.Updateable<TenantMember>()
                .SetColumns(m => new TenantMember
                {
                    DisplayName = displayName,
                    Phone = phone,
                    UnitId = unitId,
                    Role = role,
                    NotificationScope = scope,
                    Status = status,
                })
                .Where(m => m.Id == id && m.TenantId == tenantId)
                .ExecuteCommandAsync(ct);
            tran.CommitTran();

            member.DisplayName = displayName;
            member.Phone = phone;
            member.UnitId = unitId;
            member.Role = role;
            member.NotificationScope = scope;
            member.Status = status;
            await Send.OkAsync(MemberResponse.From(member), ct);
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            AddError(r => r.Phone!, "Someone in this organisation has that number already.", "phone-taken");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
        }
    }
}
