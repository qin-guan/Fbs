using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Options;
using Fbs.WebApi.Telemetry;
using Fbs.WebApi.Tenancy;
using Microsoft.Extensions.Options;
using SqlSugar;
using Telegram.Bot;

namespace Fbs.WebApi.TelegramLinks;

/// <summary>The bot's own name, which the link opens, kept once it is known.</summary>
public sealed class TelegramBotIdentity
{
    private string? _username;

    public async Task<string> UsernameAsync(TelegramBotClient bot, IOptions<TelegramOptions> options, CancellationToken ct)
    {
        if (_username is not null)
        {
            return _username;
        }

        var configured = options.Value.BotUsername;
        return _username = string.IsNullOrWhiteSpace(configured) ? (await bot.GetMe(ct)).Username ?? throw new InvalidOperationException("The bot has no username.") : configured.TrimStart('@');
    }
}

/// <summary>Links a person's account to a Telegram chat, with a link that can be used once, and for a few minutes.</summary>
public sealed class TelegramLinker(ISqlSugarClient sql, TelegramBotClient bot, IOptions<TelegramOptions> options, TelegramBotIdentity identity)
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);

    public sealed record Started(string Token, string Url, DateTimeOffset ExpiresAt);

    public sealed record Status(bool Linked, DateTimeOffset? LinkedAt);

    /// <summary>A link to open in Telegram. It replaces one that hasn't been used, and doesn't touch the chat that is linked already until it is.</summary>
    public async Task<Started> StartAsync(Guid userId, CancellationToken ct)
    {
        var token = InviteTokens.Generate();
        var hash = InviteTokens.Hash(token);
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);

        var updated = await SetTokenAsync(userId, hash, expiresAt, ct);
        if (updated == 0)
        {
            try
            {
                await sql.Insertable(new TelegramLink { Id = Guid.NewGuid(), UserId = userId, TokenHash = hash, TokenExpiresAt = expiresAt }).ExecuteCommandAsync(ct);
            }
            catch (Exception e) when (e.IsDuplicate())
            {
                // Asked twice at the same moment, and the other made the row
                await SetTokenAsync(userId, hash, expiresAt, ct);
            }
        }

        var username = await identity.UsernameAsync(bot, options, ct);
        return new Started(token, $"https://t.me/{username}?start={token}", expiresAt);
    }

    public async Task<Status> GetAsync(Guid userId, CancellationToken ct)
    {
        var link = await sql.Queryable<TelegramLink>().FirstAsync(l => l.UserId == userId, ct);
        return new Status(link?.ChatId is not null, link?.LinkedAt);
    }

    public async Task UnlinkAsync(Guid userId, CancellationToken ct) =>
        await sql.Updateable<TelegramLink>()
            .SetColumns(l => new TelegramLink { ChatId = null, LinkedAt = null, TokenHash = null, TokenExpiresAt = null })
            .Where(l => l.UserId == userId)
            .ExecuteCommandAsync(ct);

    /// <summary>
    /// Links the chat to whoever asked for the token, if it is one that is waiting and hasn't run out, and the token
    /// can't be used again. A chat is one person's: if it was linked to another account, that one is unlinked.
    /// </summary>
    /// <returns>The name of the account, or null if the token was no good.</returns>
    public async Task<string?> CompleteAsync(string token, string chatId, CancellationToken ct)
    {
        var hash = InviteTokens.Hash(token);
        var now = DateTimeOffset.UtcNow;
        Guid userId;

        try
        {
            using var tran = sql.Ado.UseTran();
            var link = await sql.Queryable<TelegramLink>().Where(l => l.TokenHash == hash && l.TokenExpiresAt > now).TranLock(DbLockType.Wait).FirstAsync(ct);
            if (link is null)
            {
                return null;
            }

            userId = link.UserId;
            await sql.Updateable<TelegramLink>().SetColumns(l => new TelegramLink { ChatId = null, LinkedAt = null }).Where(l => l.ChatId == chatId && l.UserId != userId).ExecuteCommandAsync(ct);
            await sql.Updateable<TelegramLink>()
                .SetColumns(l => new TelegramLink { ChatId = chatId, LinkedAt = now, TokenHash = null, TokenExpiresAt = null })
                .Where(l => l.Id == link.Id)
                .ExecuteCommandAsync(ct);
            tran.CommitTran();
            FbsMetrics.TelegramLinked.Add(1);
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            // Another chat was being linked to this chat at the same moment: try again
            return null;
        }

        var account = await sql.Queryable<UserAccount>().FirstAsync(a => a.Id == userId, ct);
        return account?.Name ?? account?.Email ?? "your account";
    }

    /// <summary>
    /// Connects the chat to the account for notifications, unless the account already has a chat connected or another
    /// account has this chat. Used after a claim, where the chat has just proved who the user is.
    /// </summary>
    public async Task LinkIfNoneAsync(Guid userId, string chatId, CancellationToken ct)
    {
        try
        {
            var taken = await sql.Queryable<TelegramLink>().AnyAsync(l => l.ChatId == chatId, ct);
            var link = await sql.Queryable<TelegramLink>().FirstAsync(l => l.UserId == userId, ct);
            if (taken || link?.ChatId is not null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (link is null)
            {
                await sql.Insertable(new TelegramLink { Id = Guid.NewGuid(), UserId = userId, ChatId = chatId, LinkedAt = now }).ExecuteCommandAsync(ct);
                FbsMetrics.TelegramLinked.Add(1);
            }
            else if (await sql.Updateable<TelegramLink>().SetColumns(l => new TelegramLink { ChatId = chatId, LinkedAt = now }).Where(l => l.Id == link.Id && l.ChatId == null).ExecuteCommandAsync(ct) > 0)
            {
                FbsMetrics.TelegramLinked.Add(1);
            }
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            // Another account connected this chat at the same moment, and keeps it
        }
    }

    private Task<int> SetTokenAsync(Guid userId, string hash, DateTimeOffset expiresAt, CancellationToken ct) =>
        sql.Updateable<TelegramLink>().SetColumns(l => new TelegramLink { TokenHash = hash, TokenExpiresAt = expiresAt }).Where(l => l.UserId == userId).ExecuteCommandAsync(ct);
}
