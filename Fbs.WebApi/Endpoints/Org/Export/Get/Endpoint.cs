using FastEndpoints;
using Fbs.WebApi.Auth;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.RateLimiting;
using Fbs.WebApi.Tenancy;
using Microsoft.AspNetCore.RateLimiting;
using SqlSugar;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Endpoints.Org.Export.Get;

/// <summary>
/// A copy of everything an organisation has, to keep, for its admins: who is in it, and every booking. It is the organisation's own,
/// and it is limited and written in its history, as it has the phone numbers of everyone in it.
/// </summary>
[RequiresAccounts]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql, AuditLog audit) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/t/{slug}/Export");
        AuthSchemes(AccountAuthentication.Scheme);
        PreProcessor<ResolveTenant>();
        PreProcessor<RequireAdmin>();
        Options(x => x.RequireRateLimiting(RateLimitPolicies.Export));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenant = tenantContext.Tenant;
        var tenantId = tenant.Id;

        var units = await sql.Queryable<DataUnit>().Where(u => u.TenantId == tenantId).OrderBy(u => u.Name).ToListAsync(ct);
        var facilities = await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).OrderBy(f => f.Name).ToListAsync(ct);
        var access = (await sql.Queryable<FacilityUnitAccess>().Where(a => a.TenantId == tenantId).ToListAsync(ct)).ToLookup(a => a.FacilityId, a => a.UnitId);
        var members = await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId).OrderBy(m => m.DisplayName).ToListAsync(ct);
        var bookings = await sql.Queryable<DataBooking>().Where(b => b.TenantId == tenantId).OrderBy(b => b.StartUtc).OrderBy(b => b.Id).ToListAsync(ct);
        var invites = await sql.Queryable<TenantInvite>().Where(i => i.TenantId == tenantId).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var history = await sql.Queryable<AuditEntry>().Where(e => e.TenantId == tenantId).OrderBy(e => e.At).ToListAsync(ct);

        // Taking a copy of everybody's phone numbers is something to be able to see was done, and by whom
        await audit.WriteAsync(tenantId, tenantContext.Member.Id, "tenant.exported", "Downloaded a copy of the organisation's data.", "tenant", tenantId, ct);

        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{tenant.Slug}-data.json\"";
        await Send.OkAsync(
            new Response
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Organization = new OrganizationData
                {
                    Slug = tenant.Slug,
                    Name = tenant.Name,
                    TimeZone = tenant.TimeZone,
                    DefaultCountryCode = tenant.DefaultCountryCode,
                    SlotMinutes = tenant.SlotMinutes,
                    RequireApproval = tenant.RequireApproval,
                    CreatedAt = tenant.CreatedAt,
                },
                Units = units.Select(u => new UnitData { Id = u.Id, Name = u.Name }).ToList(),
                Facilities = facilities.Select(f => new FacilityData { Id = f.Id, Name = f.Name, Group = f.Group, AvailableToAll = f.AvailableToAll, UnitIds = access[f.Id].ToList() }).ToList(),
                Members = members
                    .Select(m => new MemberData
                    {
                        Id = m.Id,
                        DisplayName = m.DisplayName,
                        Phone = m.Phone,
                        UnitId = m.UnitId,
                        Role = m.Role,
                        Status = m.Status,
                        NotificationScope = m.NotificationScope,
                        HasAccount = m.UserId is not null,
                        CreatedAt = m.CreatedAt,
                    })
                    .ToList(),
                Bookings = bookings
                    .Select(b => new BookingData
                    {
                        Id = b.Id,
                        FacilityId = b.FacilityId,
                        Start = b.StartUtc,
                        End = b.EndUtc,
                        Conduct = b.Conduct,
                        Description = b.Description,
                        PocName = b.PocName,
                        PocPhone = b.PocPhone,
                        BookedByMemberId = b.BookedByMemberId,
                        UpdatedByMemberId = b.UpdatedByMemberId,
                        UnitId = b.UnitId,
                        BatchId = b.BatchId,
                        Revision = b.Revision,
                        CreatedAt = b.CreatedAt,
                        UpdatedAt = b.UpdatedAt,
                        CancelledAt = b.CancelledAt,
                        CancelledByMemberId = b.CancelledByMemberId,
                    })
                    .ToList(),
                Invites = invites.Select(i => new InviteData { Id = i.Id, Role = i.Role, UnitId = i.UnitId, ExpiresAt = i.ExpiresAt, MaxUses = i.MaxUses, Uses = i.Uses, RevokedAt = i.RevokedAt, CreatedAt = i.CreatedAt }).ToList(),
                History = history.Select(e => new AuditData { At = e.At, ActorMemberId = e.ActorMemberId, Action = e.Action, Summary = e.Summary, TargetType = e.TargetType, TargetId = e.TargetId }).ToList(),
            },
            ct
        );
    }
}
