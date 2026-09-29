namespace Fbs.WebApi.Tenancy;

public sealed class TenantLimits
{
    /// <summary>How many organisations one person can make, so signing up isn't a way to fill the database.</summary>
    public int MaxTenantsPerUser { get; set; } = 3;
}
