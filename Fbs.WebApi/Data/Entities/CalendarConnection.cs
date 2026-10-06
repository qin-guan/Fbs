using System.Text.Json.Serialization;
using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// A Google Calendar that a tenant's bookings are copied to, one way: bookings go to the calendar, and
/// changes made in the calendar are not read back. A tenant has at most one.
/// </summary>
/// <remarks>
/// It is connected from the organization settings. A code is written into the calendar and the admin reads
/// it back, so knowing another organization's calendar id is not enough to copy bookings there. Until that
/// code is given, the status is <see cref="CalendarConnectionStatus.Pending"/> and nothing is copied.
/// </remarks>
[SugarIndex("UX_CalendarConnection_TenantId", nameof(TenantId), OrderByType.Asc, IsUnique = true)]
public class CalendarConnection
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>What Google calls the calendar, which is its email address for all but the primary one.</summary>
    [SugarColumn(Length = 256)]
    public string CalendarId { get; set; } = null!;

    public CalendarConnectionStatus Status { get; set; } = CalendarConnectionStatus.Active;

    /// <summary>Why it stopped, when <see cref="Status"/> is <see cref="CalendarConnectionStatus.Failed"/>, for whoever runs the tenant to see.</summary>
    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? LastError { get; set; }

    /// <summary>A hash of the code written into the calendar while <see cref="Status"/> is <see cref="CalendarConnectionStatus.Pending"/>. The code itself is not kept.</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? VerificationCodeHash { get; set; }

    /// <summary>When the code stops being accepted.</summary>
    [SugarColumn(IsNullable = true)]
    public DateTimeOffset? VerificationExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalendarConnectionStatus
{
    /// <summary>Bookings are copied to it.</summary>
    Active = 1,

    /// <summary>Google refused, such as when the calendar isn't shared with the service account any more. Nothing more is sent until it is put right.</summary>
    Failed = 2,

    /// <summary>Turned off.</summary>
    Disabled = 3,

    /// <summary>A code was written into the calendar, and the admin has not read it back yet. Nothing is copied.</summary>
    Pending = 4,
}
