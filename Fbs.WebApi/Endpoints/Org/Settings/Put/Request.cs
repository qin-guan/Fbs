namespace Fbs.WebApi.Endpoints.Org.Settings.Put;

public class Request
{
    public string Name { get; set; } = string.Empty;

    public string TimeZone { get; set; } = string.Empty;

    public string DefaultCountryCode { get; set; } = string.Empty;

    /// <summary>15, 30 or 60: the length of the smallest slot bookings start and end on.</summary>
    public int SlotMinutes { get; set; }

    public bool RequireApproval { get; set; }

    /// <summary>Whether imported members can still claim. Leave it out to keep it as it is. It can be turned off, but not back on.</summary>
    public bool? LegacyClaimEnabled { get; set; }
}
