namespace Fbs.WebApi.Tenancy;

public sealed class TenantLimits
{
    /// <summary>How many organisations one person can make, so signing up isn't a way to fill the database.</summary>
    public int MaxTenantsPerUser { get; set; } = 3;

    /// <summary>How many invite links an organisation can have going at once, so a link that is shared further than it should be can be seen and stopped.</summary>
    public int MaxActiveInvites { get; set; } = 20;
}
