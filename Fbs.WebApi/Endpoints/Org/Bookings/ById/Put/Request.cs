namespace Fbs.WebApi.Endpoints.Org.Bookings.ById.Put;

public class Request
{
    public Guid Id { get; set; }

    public string? Conduct { get; set; }

    public string? Description { get; set; }

    public string? PocName { get; set; }

    public string? PocPhone { get; set; }

    /// <summary>The new start. Leave both times out to keep the time it has.</summary>
    public DateTimeOffset? StartDateTime { get; set; }

    /// <summary>The new end. Leave both times out to keep the time it has.</summary>
    public DateTimeOffset? EndDateTime { get; set; }
}
