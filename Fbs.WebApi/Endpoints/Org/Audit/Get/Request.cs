using FastEndpoints;

namespace Fbs.WebApi.Endpoints.Org.Audit.Get;

public class Request
{
    /// <summary>Only what was done before this, which is how the next page is asked for: the <c>at</c> of the last of the one before.</summary>
    [QueryParam]
    public DateTimeOffset? Before { get; set; }

    /// <summary>How many, from 1 to 200. 50 if left out.</summary>
    [QueryParam]
    public int? Limit { get; set; }
}
