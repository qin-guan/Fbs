namespace Fbs.WebApi.Endpoints.Tenants.Post;

public class Request
{
    /// <summary>What it is called to people.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What it is called in addresses: <c>/t/{slug}</c>. Lower case letters, digits and hyphens.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>An IANA time zone, such as <c>Asia/Singapore</c>, that times are shown, and bookings made, in. UTC if left out.</summary>
    public string? TimeZone { get; set; }

    /// <summary>The calling code, without the plus, that phone numbers without one are taken to be in.</summary>
    public string? DefaultCountryCode { get; set; }
}
