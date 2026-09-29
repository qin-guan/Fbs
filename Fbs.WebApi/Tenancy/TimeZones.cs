namespace Fbs.WebApi.Tenancy;

public static class TimeZones
{
    /// <summary>Whether this server knows the time zone, which is what times are converted with.</summary>
    public static bool IsKnown(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
