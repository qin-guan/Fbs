using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Data;

public static class TenantTimeZone
{
    /// <summary>
    /// The tenant's time zone, or UTC when the host doesn't know it, as with a minimal image with no time zone
    /// data, so a missing zone shows times that are right but in UTC rather than stopping everything.
    /// </summary>
    public static TimeZoneInfo Of(Tenant tenant)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
