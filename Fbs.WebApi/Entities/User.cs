using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Entities;

public class User : Entity
{
    public string? Unit { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? TelegramChatId { get; set; }
    public string? NotificationGroup { get; set; }
    public bool IsAdmin { get; set; }

    public override string? GetId() => Phone;

    /// <summary>
    /// Whether this user can change or cancel bookings made by <paramref name="bookedBy"/>:
    /// their own bookings and those of anyone else in the same unit.
    /// </summary>
    public bool CanManageBookingsOf(User? bookedBy)
    {
        return (Phone is not null && Phone == bookedBy?.Phone)
            || (Unit is not null && Unit == bookedBy?.Unit);
    }
}
