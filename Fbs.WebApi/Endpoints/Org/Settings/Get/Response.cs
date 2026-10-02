namespace Fbs.WebApi.Endpoints.Org.Settings.Get;

public class Response
{
    public required string Name { get; init; }

    public required string TimeZone { get; init; }

    public required string DefaultCountryCode { get; init; }

    public required int SlotMinutes { get; init; }

    /// <summary>Whether someone who joins with an invite waits for an admin to let them in.</summary>
    public required bool RequireApproval { get; init; }
}
