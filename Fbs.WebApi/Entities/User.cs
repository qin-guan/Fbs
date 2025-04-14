namespace Fbs.WebApi.Entities;

public class User : SheetEntity
{
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public required string TelegramChatId { get; set; }
}