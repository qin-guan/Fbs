namespace Fbs.WebApi.Endpoints.Org.Me.Put;

public class Request
{
    /// <summary>What they are called in the organisation. While it is the name on their account, it changes when that does.</summary>
    public string DisplayName { get; set; } = string.Empty;
}
