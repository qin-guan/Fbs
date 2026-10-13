namespace Fbs.WebApi.Options;

public class GoogleOptions
{
    /// <summary>
    /// The service account, as its JSON key in base64, that bookings are copied to organisations' calendars as. A calendar is
    /// shared with its email to be connected.
    /// </summary>
    public required string ServiceAccountJsonCredential { get; set; }
}
