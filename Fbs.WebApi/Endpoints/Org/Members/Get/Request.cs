using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Org.Members.Get;

public class Request
{
    /// <summary>Also the people who have left, who are left out unless this is set.</summary>
    [QueryParam]
    public bool IncludeRemoved { get; set; }
}
