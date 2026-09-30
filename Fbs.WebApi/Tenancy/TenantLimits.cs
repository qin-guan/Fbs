namespace Fbs.WebApi.Tenancy;

public sealed class TenantLimits
{
    /// <summary>How many organisations one person can make, so signing up isn't a way to fill the database.</summary>
    public int MaxTenantsPerUser { get; set; } = 3;

    /// <summary>How many invite links an organisation can have going at once, so a link that is shared further than it should be can be seen and stopped.</summary>
    public int MaxActiveInvites { get; set; } = 20;

    /// <summary>How many units an organisation can have.</summary>
    public int MaxUnits { get; set; } = 50;

    /// <summary>How many facilities an organisation can have.</summary>
    public int MaxFacilities { get; set; } = 100;

    /// <summary>
    /// How many people an organisation can have, counting those waiting to be let in and those added by phone number, and not
    /// those who have been removed. Organisations are meant to have 20 to 50.
    /// </summary>
    public int MaxMembers { get; set; } = 500;

    /// <summary>
    /// How many bookings an organisation can make in 24 hours, counting ones that were cancelled. One that is being made in
    /// several slots is that many. It is a limit on how fast the database can be filled, not on how many an organisation can have.
    /// </summary>
    public int MaxBookingsPerDay { get; set; } = 1000;

    /// <summary>How many days an organisation that an admin has deleted can be restored for, before it is deleted for good.</summary>
    public int DeletionGraceDays { get; set; } = 30;
}
