using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// The Telegram chat a person gets their notifications in, across every organisation they are in. It is linked from
/// the app: the person asks for a link, and opening it in Telegram tells the bot which account it is for.
/// </summary>
/// <remarks>
/// There is a row per person from the first time they ask. <see cref="ChatId"/> is set once the link is opened, and
/// <see cref="TokenHash"/> is only there while one is waiting to be, so it can be used once.
/// </remarks>
[SugarIndex("UX_TelegramLink_UserId", nameof(UserId), OrderByType.Asc, IsUnique = true)]
[SugarIndex("UX_TelegramLink_ChatId", nameof(ChatId), OrderByType.Asc, IsUnique = true)]
[SugarIndex("UX_TelegramLink_TokenHash", nameof(TokenHash), OrderByType.Asc, IsUnique = true)]
public class TelegramLink
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? ChatId { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTimeOffset? LinkedAt { get; set; }

    /// <summary>SHA-256 of the token that is waiting to be used, in lower case hex.</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? TokenHash { get; set; }

    [SugarColumn(ColumnDataType = "datetime(6)", IsNullable = true)]
    public DateTimeOffset? TokenExpiresAt { get; set; }
}
