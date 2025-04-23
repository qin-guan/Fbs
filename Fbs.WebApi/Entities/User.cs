using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Entities;

public class User : Entity
{
    public string? Unit { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? TelegramChatId { get; set; }
    public string? NotificationGroup { get; set; }

    public override string? GetId() => Phone;
}