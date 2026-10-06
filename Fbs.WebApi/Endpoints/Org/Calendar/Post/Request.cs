namespace Fbs.WebApi.Endpoints.Org.Calendar.Post;

public class Request
{
    /// <summary>The calendar's id, as Google shows it under the calendar's settings.</summary>
    public string CalendarId { get; set; } = string.Empty;
}