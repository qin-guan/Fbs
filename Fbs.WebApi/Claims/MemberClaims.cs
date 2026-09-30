using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Options;
using Fbs.WebApi.TelegramLinks;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Options;
using SqlSugar;
using Telegram.Bot;

namespace Fbs.WebApi.Claims;

public enum ClaimOutcome
{
    Claimed = 1,

    /// <summary>The link is unknown, has been used, or has run out.</summary>
    NoSuchLink = 2,

    /// <summary>Claiming is off for the organisation, or the organisation isn't there.</summary>
    Unavailable = 3,

    /// <summary>No place was linked to the chat, or more than one was, so it can't be told which is theirs.</summary>
    NoPlaceForChat = 4,

    /// <summary>The account already has a place in the organisation.</summary>
    AlreadyMember = 5,
}

public sealed record ClaimResult(ClaimOutcome Outcome, string? MemberName = null, string? OrganizationName = null);

/// <summary>
/// Lets somebody who was in the old version, and is signed in with an account now, take over their place: by opening a
/// link in the Telegram chat their place was linked to, which shows they control it.
/// </summary>
/// <remarks>
/// The place is kept as it was, apart from the role: whoever is claiming is a member even if they were an admin, as
/// which chat was linked to them can't be relied on for that, and an admin is made by somebody who runs the system
/// (see <see cref="MemberPromotions"/>).
/// </remarks>
public sealed class MemberClaims(ISqlSugarClient sql, TelegramBotClient bot, IOptions<TelegramOptions> options, TelegramBotIdentity identity, TelegramLinker linker)
{
    public const string StartPrefix = "claim_";

    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);

    public sealed record Started(string Url, DateTimeOffset ExpiresAt);

    /// <summary>The organisation someone can claim their place in, if claiming is on for it, and they haven't a place in it already.</summary>
    public async Task<Tenant?> ClaimableAsync(string slug, Guid userId, CancellationToken ct)
    {
        var tenant = await sql.Queryable<Tenant>().FirstAsync(t => t.Slug == slug && t.LegacyClaimEnabled && t.Status == TenantStatus.Active, ct);
        if (tenant is null)
        {
            return null;
        }

        var tenantId = tenant.Id;
        return await sql.Queryable<TenantMember>().AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, ct) ? null : tenant;
    }

    /// <summary>A link to open in Telegram. It replaces one made for the same organisation that wasn't used.</summary>
    public async Task<Started> StartAsync(Guid userId, Tenant tenant, CancellationToken ct)
    {
        var token = InviteTokens.Generate();
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        var tenantId = tenant.Id;

        await sql.Deleteable<MemberClaimToken>().Where(t => t.UserId == userId && t.TenantId == tenantId && t.UsedAt == null).ExecuteCommandAsync(ct);
        await sql.Insertable(new MemberClaimToken { Id = Guid.NewGuid(), UserId = userId, TenantId = tenantId, TokenHash = InviteTokens.Hash(token), ExpiresAt = expiresAt }).ExecuteCommandAsync(ct);

        var username = await identity.UsernameAsync(bot, options, ct);
        return new Started($"https://t.me/{username}?start={StartPrefix}{token}", expiresAt);
    }

    /// <summary>Gives the place that was linked to the chat to the account the token was made for.</summary>
    public async Task<ClaimResult> CompleteAsync(string token, string chatId, CancellationToken ct)
    {
        var result = await TransactionRetry.RunAsync(() => CompleteOnceAsync(token, chatId, ct), ct);
        if (result.Outcome == ClaimOutcome.Claimed && result.UserId is { } userId)
        {
            // The chat they proved they control is where they are told from now on, unless they have one connected already
            await linker.LinkIfNoneAsync(userId, chatId, ct);
        }

        return new ClaimResult(result.Outcome, result.MemberName, result.OrganizationName);
    }

    private sealed record Completed(ClaimOutcome Outcome, Guid? UserId = null, string? MemberName = null, string? OrganizationName = null);

    private async Task<Completed> CompleteOnceAsync(string token, string chatId, CancellationToken ct)
    {
        var hash = InviteTokens.Hash(token);
        var now = DateTimeOffset.UtcNow;

        try
        {
            using var tran = sql.Ado.UseTran();
            var claim = await sql.Queryable<MemberClaimToken>().Where(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now).TranLock(DbLockType.Wait).FirstAsync(ct);
            if (claim is null)
            {
                return new Completed(ClaimOutcome.NoSuchLink);
            }

            var tenantId = claim.TenantId;
            var tenant = await sql.Queryable<Tenant>().FirstAsync(t => t.Id == tenantId && t.LegacyClaimEnabled && t.Status == TenantStatus.Active, ct);
            if (tenant is null)
            {
                return new Completed(ClaimOutcome.Unavailable);
            }

            var unclaimed = MemberStatus.Unclaimed;
            var places = await sql.Queryable<TenantMember>()
                .Where(m => m.TenantId == tenantId && m.LegacyChatId == chatId && m.UserId == null && m.Status == unclaimed)
                .TranLock(DbLockType.Wait)
                .ToListAsync(ct);
            if (places.Count != 1)
            {
                // The link is left as it is, so they can open it from the right chat
                return new Completed(ClaimOutcome.NoPlaceForChat, OrganizationName: tenant.Name);
            }

            var place = places[0];
            var userId = claim.UserId;
            var claimId = claim.Id;
            await sql.Updateable<TenantMember>()
                .SetColumns(m => new TenantMember { UserId = userId, Status = MemberStatus.Active, Role = MemberRole.Member })
                .Where(m => m.Id == place.Id)
                .ExecuteCommandAsync(ct);
            await sql.Updateable<MemberClaimToken>().SetColumns(t => new MemberClaimToken { UsedAt = now }).Where(t => t.Id == claimId).ExecuteCommandAsync(ct);
            tran.CommitTran();

            return new Completed(ClaimOutcome.Claimed, userId, place.DisplayName, tenant.Name);
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            // They have a place in the organisation already
            return new Completed(ClaimOutcome.AlreadyMember);
        }
    }
}
