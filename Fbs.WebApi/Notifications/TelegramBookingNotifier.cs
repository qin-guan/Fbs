using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using SqlSugar;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Notifications;

/// <summary>
/// Tells people on Telegram that bookings were made, changed or cancelled: the booker, and whoever has
/// asked to hear about everyone's bookings, or their unit's.
/// </summary>
/// <remarks>
/// Bookings made together are told about as one message. People with no Telegram are left out, and so are
/// those who have blocked the bot, which is nothing to try again for. Anything else that goes wrong is
/// tried again later, without telling twice the people who have been told already.
/// </remarks>
public sealed class TelegramBookingNotifier(
    ISqlSugarClient sql,
    TelegramBotClient bot,
    TelegramThrottle throttle,
    ILogger<TelegramBookingNotifier> logger
) : IOutboxHandler
{
    public const string MessageType = "telegram.booking";

    /// <summary>Messages are sent a few at a time, so telling a lot of people doesn't take as long as their round trips added up.</summary>
    private const int MaxParallelSends = 8;

    public string Type => MessageType;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = Parse(message);
        var tenantId = message.TenantId;
        var tenant =
            await sql.Queryable<Tenant>().FirstAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new OutboxPermanentFailureException($"Tenant {tenantId} does not exist.");
        var bookingIds = payload.BookingIds;
        var rows = await sql.Queryable<DataBooking>()
            .Where(b => b.TenantId == tenantId && bookingIds.Contains(b.Id))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            throw new OutboxPermanentFailureException("None of the bookings exist.");
        }

        var facilities = (await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(f => f.Id);
        var members = (await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(m => m.Id);
        var units = (await sql.Queryable<DataUnit>().Where(u => u.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        var zone = TenantTimeZone.Of(tenant);

        // In the order they were made in, which the message lists them in
        var byId = rows.ToDictionary(b => b.Id);
        var bookings = payload.BookingIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var first = bookings[0];
        var booker = members.GetValueOrDefault(first.BookedByMemberId)
            ?? throw new OutboxPermanentFailureException($"Member {first.BookedByMemberId} does not exist.");
        var actor = members.GetValueOrDefault(payload.ActorMemberId) ?? booker;

        var text = TelegramBookingMessages.Build(
            payload.Change,
            bookings
                .Select(b => new BookingSummary(
                    b.Id,
                    facilities.GetValueOrDefault(b.FacilityId)?.Name ?? "Unknown facility",
                    TimeZoneInfo.ConvertTime(b.StartUtc, zone),
                    TimeZoneInfo.ConvertTime(b.EndUtc, zone)
                ))
                .ToList(),
            first.Conduct,
            first.Description,
            first.PocName,
            first.PocPhone,
            new Person(
                actor.UnitId is { } unitId && units.TryGetValue(unitId, out var unitName) ? unitName : null,
                actor.DisplayName,
                PhoneNumbers.ToApi(actor.Phone)
            ),
            payload.PreviousStartUtc is { } start ? TimeZoneInfo.ConvertTime(start, zone) : null,
            payload.PreviousEndUtc is { } end ? TimeZoneInfo.ConvertTime(end, zone) : null,
            first.BatchId
        );

        var unit = first.UnitId ?? booker.UnitId;
        var chats = await ChatsOfAsync(members.Values, cancellationToken);
        var inSeveral = await InSeveralOrganizationsAsync(members.Values, cancellationToken);
        var heading = $"<b>{WebUtility.HtmlEncode(tenant.Name)}</b>\n";

        var recipients = members
            .Values.Where(m => m.Status is MemberStatus.Active or MemberStatus.Unclaimed)
            .Where(m =>
                m.Id == booker.Id
                || m.NotificationScope == NotificationScope.All
                || (m.NotificationScope == NotificationScope.Unit && unit is not null && m.UnitId == unit)
            )
            .Where(m => !payload.Delivered.Contains(m.Id))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new Recipient(m, chats.GetValueOrDefault(m.Id), m.UserId is { } userId && inSeveral.Contains(userId) ? heading + text : text))
            .Where(r => !string.IsNullOrWhiteSpace(r.Chat))
            .ToList();

        await SendAsync(message, payload, recipients, cancellationToken);
    }

    /// <summary>The chat each member is told in: the one their account has linked, or else the one their phone number was linked to.</summary>
    private async Task<Dictionary<Guid, string>> ChatsOfAsync(IEnumerable<TenantMember> members, CancellationToken cancellationToken)
    {
        var people = members.ToList();
        var userIds = people.Where(m => m.UserId is not null).Select(m => m.UserId).Distinct().ToList();
        var links = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await sql.Queryable<TelegramLink>().Where(l => userIds.Contains(l.UserId) && l.ChatId != null).ToListAsync(cancellationToken)).ToDictionary(l => l.UserId, l => l.ChatId!);

        var chats = new Dictionary<Guid, string>();
        foreach (var member in people)
        {
            if (member.UserId is { } userId && links.TryGetValue(userId, out var linked))
            {
                chats[member.Id] = linked;
            }
            else if (!string.IsNullOrWhiteSpace(member.LegacyChatId))
            {
                chats[member.Id] = member.LegacyChatId;
            }
        }

        return chats;
    }

    /// <summary>The accounts that belong to more than one organisation, who are told which one a booking is in.</summary>
    private async Task<HashSet<Guid>> InSeveralOrganizationsAsync(IEnumerable<TenantMember> members, CancellationToken cancellationToken)
    {
        var userIds = members.Where(m => m.UserId is not null).Select(m => m.UserId).Distinct().ToList();
        if (userIds.Count == 0)
        {
            return [];
        }

        var active = MemberStatus.Active;
        var memberships = await sql.Queryable<TenantMember>().Where(m => userIds.Contains(m.UserId) && m.Status == active).Select(m => new { m.UserId, m.TenantId }).ToListAsync(cancellationToken);
        return memberships.GroupBy(m => m.UserId!.Value).Where(g => g.Select(m => m.TenantId).Distinct().Count() > 1).Select(g => g.Key).ToHashSet();
    }

    private sealed record Recipient(TenantMember Member, string? Chat, string Text);

    private async Task SendAsync(
        OutboxMessage message,
        TelegramBookingPayload payload,
        List<Recipient> recipients,
        CancellationToken cancellationToken
    )
    {
        var told = new ConcurrentQueue<Guid>();
        var failures = new ConcurrentQueue<Exception>();

        await Parallel.ForEachAsync(
            recipients,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelSends, CancellationToken = cancellationToken },
            async (recipient, token) =>
            {
                var member = recipient.Member;
                try
                {
                    await throttle.WaitAsync(token);
                    await bot.SendMessage(ChatIdOf(recipient.Chat!), recipient.Text, ParseMode.Html, cancellationToken: token);
                    told.Enqueue(member.Id);
                }
                catch (ApiRequestException e) when (e.ErrorCode is 400 or 403)
                {
                    // Blocked the bot, or the chat is gone: nothing to try again
                    logger.LogWarning("Could not tell {Member} about {Change}: {Error}", member.Id, payload.Change, e.Message);
                    told.Enqueue(member.Id);
                }
                catch (Exception e) when (!token.IsCancellationRequested)
                {
                    failures.Enqueue(e);
                }
            }
        );

        if (failures.IsEmpty)
        {
            return;
        }

        // Keep who was told, so trying again doesn't tell them twice
        payload.Delivered.AddRange(told);
        var json = JsonSerializer.Serialize(payload);
        var id = message.Id;
        await sql.Updateable<OutboxMessage>()
            .SetColumns(m => new OutboxMessage { Payload = json })
            .Where(m => m.Id == id)
            .ExecuteCommandAsync(cancellationToken);

        throw failures.Count == 1 ? failures.First() : new AggregateException(failures);
    }

    private static TelegramBookingPayload Parse(OutboxMessage message)
    {
        try
        {
            return JsonSerializer.Deserialize<TelegramBookingPayload>(message.Payload)
                ?? throw new OutboxPermanentFailureException("The message is empty.");
        }
        catch (JsonException e)
        {
            throw new OutboxPermanentFailureException("The message could not be read.", e);
        }
    }

    /// <summary>A chat is its number, and a channel or group its name.</summary>
    private static ChatId ChatIdOf(string chat) => long.TryParse(chat, out var id) ? new ChatId(id) : new ChatId(chat);
}
