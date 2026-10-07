using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Options;
using Fbs.WebApi.TelegramLinks;
using Fbs.WebApi.Telemetry;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Options;
using SqlSugar;
using Telegram.Bot;

namespace Fbs.WebApi.Claims;

public enum ClaimOutcome
{
    Claimed = 1,

    /// <summary>The token is unknown, already used, or expired.</summary>
    NoSuchLink = 2,

    /// <summary>The organisation doesn't exist, isn't active, or has claiming turned off.</summary>
    Unavailable = 3,

    /// <summary>
    /// No unclaimed member has this chat as their <see cref="TenantMember.LegacyChatId"/>, or more than one does. The token
    /// is not used up, so it can still be opened from the right chat.
    /// </summary>
    NoMemberForChat = 4,

    /// <summary>The account is already a member of the organisation.</summary>
    AlreadyMember = 5,
}

public sealed record ClaimResult(ClaimOutcome Outcome, string? MemberName = null, string? OrganizationName = null);

/// <summary>
/// Claiming: attaching a Clerk account to a member imported from the old version (status
/// <see cref="MemberStatus.Unclaimed"/>, no <see cref="TenantMember.UserId"/>).
/// </summary>
/// <remarks>
/// <para>
/// The old version knew people by phone number and sent their login codes to a Telegram chat, which the import kept as
/// <see cref="TenantMember.LegacyChatId"/>. To claim, a signed-in user asks for a one-time Telegram link
/// (<see cref="StartAsync"/>) and opens it. Telegram then sends <c>/start claim_&lt;token&gt;</c> to the bot from their chat,
/// and <see cref="CompleteAsync"/> gives the account the member whose <c>LegacyChatId</c> is that chat. Being able to send from
/// the chat is the proof of identity.
/// </para>
/// <para>
/// The member keeps their phone, unit and notification settings, but always becomes a <see cref="MemberRole.Member"/>, even
/// if they were an admin. The old bot let anyone link their chat to another person's phone number, so a
/// <c>LegacyChatId</c> is not trusted with admin rights. Admins are made with <see cref="MemberPromotions"/>.
/// See docs/runbooks/cutover-2-accounts.md.
/// </para>
/// </remarks>
public sealed class MemberClaims(ISqlSugarClient sql, TelegramBotClient bot, IOptions<TelegramOptions> options, TelegramBotIdentity identity, TelegramLinker linker)
{
    public const string StartPrefix = "claim_";

    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);

    public sealed record Started(string Url, DateTimeOffset ExpiresAt);

    /// <summary>
    /// The organisation with this slug, if the user can claim in it: it is active, has claiming on, and the user is not a member
    /// of it already. Otherwise null.
    /// </summary>
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

    /// <summary>
    /// Makes the Telegram link to open, valid once for <see cref="TokenLifetime"/>. Only a hash of the token is stored, and any
    /// unused token the user had for this organisation is deleted.
    /// </summary>
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

    /// <summary>
    /// Called by the bot when <paramref name="chatId"/> sends <c>/start claim_&lt;token&gt;</c>. Attaches the account the token was made
    /// for to the unclaimed member whose <c>LegacyChatId</c> is <paramref name="chatId"/>, and uses up the token.
    /// </summary>
    public async Task<ClaimResult> CompleteAsync(string token, string chatId, CancellationToken ct)
    {
        var result = await TransactionRetry.RunAsync(() => CompleteOnceAsync(token, chatId, ct), ct);
        if (result.Outcome == ClaimOutcome.Claimed && result.UserId is { } userId)
        {
            // Send their booking notifications to this chat, unless their account already has one connected
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
            // Locked, so a token is used at most once even if it is opened twice at the same moment
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

            // Locked, so that if several accounts open their links from this chat at the same moment, only one gets the member
            var unclaimed = MemberStatus.Unclaimed;
            var places = await sql.Queryable<TenantMember>()
                .Where(m => m.TenantId == tenantId && m.LegacyChatId == chatId && m.UserId == null && m.Status == unclaimed)
                .TranLock(DbLockType.Wait)
                .ToListAsync(ct);
            if (places.Count != 1)
            {
                // Leave the token unused, so they can still open it from the right chat
                return new Completed(ClaimOutcome.NoMemberForChat, OrganizationName: tenant.Name);
            }

            var place = places[0];
            var userId = claim.UserId;
            var claimId = claim.Id;
            // Always Member, never Admin: see the remarks on the class
            await sql.Updateable<TenantMember>()
                .SetColumns(m => new TenantMember { UserId = userId, Status = MemberStatus.Active, Role = MemberRole.Member })
                .Where(m => m.Id == place.Id)
                .ExecuteCommandAsync(ct);
            await sql.Updateable<MemberClaimToken>().SetColumns(t => new MemberClaimToken { UsedAt = now }).Where(t => t.Id == claimId).ExecuteCommandAsync(ct);
            await new AuditLog(sql).WriteAsync(tenantId, place.Id, "member.claimed", "Took over their place from the previous version.", "member", place.Id, ct);
            tran.CommitTran();
            FbsMetrics.PlacesClaimed.Add(1);

            return new Completed(ClaimOutcome.Claimed, userId, place.DisplayName, tenant.Name);
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            // The unique index on (TenantId, UserId): the account is already a member of this organisation
            return new Completed(ClaimOutcome.AlreadyMember);
        }
    }
}
