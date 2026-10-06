using System.Text.Json.Serialization;
using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Org.Calendar;

public class Response
{
    public CalendarLinkStatus Status { get; set; }

    /// <summary>What Google calls the calendar. None until one is asked for.</summary>
    public string? CalendarId { get; set; }

    /// <summary>Why copying stopped, when <see cref="Status"/> is <see cref="CalendarLinkStatus.Failed"/>.</summary>
    public string? LastError { get; set; }

    /// <summary>When the code in the calendar stops being accepted, while it is waiting.</summary>
    public DateTimeOffset? VerificationExpiresAt { get; set; }

    /// <summary>The account the calendar has to be shared with. Missing when this app has no service account to name.</summary>
    public string? ServiceAccountEmail { get; set; }

    public static Response From(CalendarConnection? connection, string? serviceAccountEmail) =>
        new()
        {
            Status = connection?.Status switch
            {
                CalendarConnectionStatus.Pending => CalendarLinkStatus.Pending,
                CalendarConnectionStatus.Active => CalendarLinkStatus.Active,
                CalendarConnectionStatus.Failed => CalendarLinkStatus.Failed,
                CalendarConnectionStatus.Disabled => CalendarLinkStatus.Disabled,
                _ => CalendarLinkStatus.None,
            },
            CalendarId = connection?.CalendarId,
            LastError = connection is { Status: CalendarConnectionStatus.Failed } ? connection.LastError : null,
            VerificationExpiresAt = connection is { Status: CalendarConnectionStatus.Pending } ? connection.VerificationExpiresAt : null,
            ServiceAccountEmail = serviceAccountEmail,
        };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalendarLinkStatus
{
    None,
    Pending,
    Active,
    Failed,
    Disabled,
}