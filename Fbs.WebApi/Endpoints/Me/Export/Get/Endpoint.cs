using FastEndpoints;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SqlSugar;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Endpoints.Me.Export.Get;

/// <summary>
/// Everything that is kept about the person signed in, to keep: who they are here, the organisations they are in, and what they
/// booked. A copy of it, as a file to download, is theirs to ask for. It is limited, as it takes some finding.
/// </summary>
[RequiresClerk]
public class Endpoint(ICurrentAccount currentAccount, ISqlSugarClient sql) : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/Me/Export");
        AuthSchemes(ClerkAuthentication.Scheme);
        Options(x => x.RequireRateLimiting(RateLimitPolicies.Export));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var account = await currentAccount.GetAsync(ct);
        if (account is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var accountId = account.Id;
        var link = await sql.Queryable<TelegramLink>().FirstAsync(l => l.UserId == accountId, ct);
        var members = await sql.Queryable<TenantMember>().Where(m => m.UserId == accountId).ToListAsync(ct);
        var memberIds = members.Select(m => m.Id).ToList();
        var tenantIds = members.Select(m => m.TenantId).Distinct().ToList();

        var tenants = tenantIds.Count == 0 ? [] : (await sql.Queryable<Tenant>().Where(t => tenantIds.Contains(t.Id)).ToListAsync(ct)).ToDictionary(t => t.Id);
        var units = tenantIds.Count == 0 ? [] : (await sql.Queryable<DataUnit>().Where(u => tenantIds.Contains(u.TenantId)).ToListAsync(ct)).ToDictionary(u => u.Id, u => u.Name);
        var facilities = tenantIds.Count == 0 ? [] : (await sql.Queryable<DataFacility>().Where(f => tenantIds.Contains(f.TenantId)).ToListAsync(ct)).ToDictionary(f => f.Id, f => f.Name);
        var made = memberIds.Count == 0 ? [] : await sql.Queryable<DataBooking>().Where(b => memberIds.Contains(b.BookedByMemberId)).OrderBy(b => b.StartUtc).ToListAsync(ct);
        var touched = memberIds.Count == 0
            ? []
            : await sql.Queryable<DataBooking>()
                .Where(b => !memberIds.Contains(b.BookedByMemberId) && ((b.CancelledByMemberId != null && memberIds.Contains(b.CancelledByMemberId.Value)) || (b.UpdatedByMemberId != null && memberIds.Contains(b.UpdatedByMemberId.Value))))
                .OrderBy(b => b.StartUtc)
                .ToListAsync(ct);

        string SlugOf(Guid tenantId) => tenants.TryGetValue(tenantId, out var tenant) ? tenant.Slug : string.Empty;
        string? FacilityOf(Guid id) => facilities.GetValueOrDefault(id);

        HttpContext.Response.Headers.ContentDisposition = "attachment; filename=\"my-data.json\"";
        await Send.OkAsync(
            new Response
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Account = new AccountData { Id = account.Id, Name = account.Name, Email = account.Email, CreatedAt = account.CreatedAt },
                Telegram = new TelegramData { Linked = link?.ChatId is not null, ChatId = link?.ChatId, LinkedAt = link?.LinkedAt },
                Memberships = members
                    .Where(m => tenants.ContainsKey(m.TenantId))
                    .Select(m => new MembershipData
                    {
                        OrganizationSlug = tenants[m.TenantId].Slug,
                        OrganizationName = tenants[m.TenantId].Name,
                        MemberId = m.Id,
                        DisplayName = m.DisplayName,
                        Phone = m.Phone,
                        Unit = m.UnitId is { } unit ? units.GetValueOrDefault(unit) : null,
                        Role = m.Role,
                        Status = m.Status,
                        NotificationScope = m.NotificationScope,
                        JoinedAt = m.CreatedAt,
                    })
                    .OrderBy(m => m.OrganizationName)
                    .ToList(),
                Bookings = made
                    .Select(b => new BookingData
                    {
                        OrganizationSlug = SlugOf(b.TenantId),
                        Id = b.Id,
                        Facility = FacilityOf(b.FacilityId),
                        Start = b.StartUtc,
                        End = b.EndUtc,
                        Conduct = b.Conduct,
                        Description = b.Description,
                        PocName = b.PocName,
                        PocPhone = b.PocPhone,
                        MadeAt = b.CreatedAt,
                        CancelledAt = b.CancelledAt,
                    })
                    .ToList(),
                OthersBookingsTheyChanged = touched
                    .Select(b => new ChangeData
                    {
                        OrganizationSlug = SlugOf(b.TenantId),
                        Id = b.Id,
                        Facility = FacilityOf(b.FacilityId),
                        Start = b.StartUtc,
                        End = b.EndUtc,
                        What = b.CancelledByMemberId is { } by && memberIds.Contains(by) ? "cancelled" : "changed",
                    })
                    .ToList(),
            },
            ct
        );
    }
}
