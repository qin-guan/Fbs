using System.Net;
using System.Text;
using System.Text.Json;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Options;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Tenancy;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Fbs.WebApi.CalendarSync;

/// <summary>Why a calendar was not connected, for the endpoint to send back.</summary>
/// <param name="Field">The request field it is about, <c>calendarId</c> or <c>code</c>, or none.</param>
public sealed record CalendarRefusal(int Status, string Message, string Code, string? Field);

/// <summary>A connection, or why it was refused.</summary>
public sealed record CalendarAttempt(CalendarConnection? Connection, CalendarRefusal? Refusal);

/// <summary>
/// Connects a tenant's Google Calendar. The service account writes a code into the calendar, and the
/// connection starts only when an admin types that code back, so one organization cannot point at
/// another's calendar by knowing its id.
/// </summary>
public sealed class CalendarConnector(
    ISqlSugarClient sql,
    CalendarService calendar,
    OutboxSignal signal,
    AuditLog audit,
    IOptions<GoogleOptions> google,
    ILogger<CalendarConnector> logger
)
{
    public async Task<(CalendarConnection? Connection, string? ServiceAccountEmail)> DescribeAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await sql.Queryable<CalendarConnection>().FirstAsync(c => c.TenantId == tenantId, ct);
        return (connection, ServiceAccountEmail());
    }

    public async Task<CalendarAttempt> StartAsync(Guid tenantId, Guid actorMemberId, string calendarId, CancellationToken ct)
    {
        calendarId = calendarId.Trim();
        if (!IsCalendarId(calendarId))
        {
            return Refuse(StatusCodes.Status400BadRequest, "The calendar id is the address of the calendar, up to 256 characters, and not primary.", "invalid", "calendarId");
        }

        var own = await sql.Queryable<CalendarConnection>().FirstAsync(c => c.TenantId == tenantId, ct);
        if (own is { Status: CalendarConnectionStatus.Active })
        {
            return Refuse(StatusCodes.Status409Conflict, "This organization already has a calendar. Stop copying to it before connecting another.", "calendar-connected");
        }

        if (await sql.Queryable<CalendarConnection>().AnyAsync(c => c.CalendarId == calendarId && c.TenantId != tenantId, ct))
        {
            return Refuse(StatusCodes.Status409Conflict, "That calendar is already connected to an organization.", "calendar-taken", "calendarId");
        }

        var code = CalendarVerification.NewCode();
        var hash = CalendarVerification.Hash(code);
        var expires = DateTimeOffset.UtcNow.Add(CalendarVerification.Lifetime);
        var eventId = CalendarVerification.EventId(tenantId);
        var previousCalendarId = own?.CalendarId;

        try
        {
            await WriteCodeAsync(calendarId, eventId, code, ct);
        }
        catch (GoogleApiException e) when (IsPermanent(e))
        {
            logger.LogWarning(e, "Google Calendar refused calendar {CalendarId} for tenant {TenantId}", calendarId, tenantId);
            return Refuse(StatusCodes.Status400BadRequest, "Google Calendar refused that calendar. Share it with the service account and check the id.", "calendar-refused", "calendarId");
        }
        catch (GoogleApiException e)
        {
            logger.LogWarning(e, "Google Calendar did not take the check for tenant {TenantId}", tenantId);
            return Refuse(StatusCodes.Status503ServiceUnavailable, "Google Calendar didn't answer. Try again in a moment.", "calendar-unavailable");
        }

        var id = own?.Id ?? Guid.NewGuid();
        var createdAt = own?.CreatedAt ?? DateTimeOffset.UtcNow;
        try
        {
            using var tran = sql.Ado.UseTran();
            if (own is null)
            {
                await sql.Insertable(new CalendarConnection
                    {
                        Id = id,
                        TenantId = tenantId,
                        CalendarId = calendarId,
                        Status = CalendarConnectionStatus.Pending,
                        VerificationCodeHash = hash,
                        VerificationExpiresAt = expires,
                        CreatedAt = createdAt,
                    })
                    .ExecuteCommandAsync(ct);
            }
            else
            {
                var updated = await sql.Updateable<CalendarConnection>()
                    .SetColumns(c => new CalendarConnection
                    {
                        CalendarId = calendarId,
                        Status = CalendarConnectionStatus.Pending,
                        LastError = null,
                        VerificationCodeHash = hash,
                        VerificationExpiresAt = expires,
                    })
                    .Where(c => c.Id == id && c.TenantId == tenantId && c.Status != CalendarConnectionStatus.Active)
                    .ExecuteCommandAsync(ct);
                if (updated == 0)
                {
                    await DeleteQuietlyAsync(calendarId, eventId, ct);
                    return Refuse(StatusCodes.Status409Conflict, "This organization already has a calendar. Stop copying to it before connecting another.", "calendar-connected");
                }
            }

            await audit.WriteAsync(tenantId, actorMemberId, "calendar.started", "Asked to connect a Google Calendar.", "calendar", id, ct);
            tran.CommitTran();
        }
        catch (Exception e) when (e.IsDuplicate())
        {
            // A second request connected this organization first. The event it wrote is the one to keep
            if (own is null)
            {
                await DeleteQuietlyAsync(calendarId, eventId, ct);
            }

            return Refuse(StatusCodes.Status409Conflict, "This organization was connected at the same time. Try again.", "calendar-connected");
        }
        catch
        {
            if (own is null)
            {
                await DeleteQuietlyAsync(calendarId, eventId, ct);
            }

            throw;
        }

        if (previousCalendarId is not null && previousCalendarId != calendarId)
        {
            await DeleteQuietlyAsync(previousCalendarId, eventId, ct);
        }

        return new CalendarAttempt(
            new CalendarConnection
            {
                Id = id,
                TenantId = tenantId,
                CalendarId = calendarId,
                Status = CalendarConnectionStatus.Pending,
                VerificationExpiresAt = expires,
                CreatedAt = createdAt,
            },
            null
        );
    }

    public async Task<CalendarAttempt> ConfirmAsync(Guid tenantId, Guid actorMemberId, string code, CancellationToken ct)
    {
        var connection = await sql.Queryable<CalendarConnection>().FirstAsync(c => c.TenantId == tenantId, ct);
        if (connection is not { Status: CalendarConnectionStatus.Pending })
        {
            return Refuse(StatusCodes.Status400BadRequest, "There is no code waiting. Ask to connect the calendar first.", "code-missing", "code");
        }

        if (connection.VerificationExpiresAt is not { } expires || expires <= DateTimeOffset.UtcNow || !CalendarVerification.Matches(connection.VerificationCodeHash, code))
        {
            return Refuse(StatusCodes.Status400BadRequest, "That code is not the one in the calendar, or it has run out. Start again.", "code-wrong", "code");
        }

        var id = connection.Id;
        var hash = connection.VerificationCodeHash;
        using (var tran = sql.Ado.UseTran())
        {
            var updated = await sql.Updateable<CalendarConnection>()
                .SetColumns(c => new CalendarConnection
                {
                    Status = CalendarConnectionStatus.Active,
                    LastError = null,
                    VerificationCodeHash = null,
                    VerificationExpiresAt = null,
                })
                .Where(c => c.Id == id && c.TenantId == tenantId && c.Status == CalendarConnectionStatus.Pending && c.VerificationCodeHash == hash)
                .ExecuteCommandAsync(ct);
            if (updated == 0)
            {
                return Refuse(StatusCodes.Status400BadRequest, "There is no code waiting. Ask to connect the calendar first.", "code-missing", "code");
            }

            await audit.WriteAsync(tenantId, actorMemberId, "calendar.connected", "Connected a Google Calendar, and bookings are copied to it.", "calendar", id, ct);
            tran.CommitTran();
        }

        await DeleteQuietlyAsync(connection.CalendarId, CalendarVerification.EventId(tenantId), ct);
        try
        {
            await CalendarSyncPlanner.EnqueueOutOfDateAsync(sql, signal, tenantId, cancellationToken: ct);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Connected tenant {TenantId}'s calendar, and queueing the bookings it is missing failed", tenantId);
        }

        connection.Status = CalendarConnectionStatus.Active;
        connection.LastError = null;
        connection.VerificationCodeHash = null;
        connection.VerificationExpiresAt = null;
        return new CalendarAttempt(connection, null);
    }

    /// <summary>Stops copying. Events already on the calendar are left there: they belong to whoever owns the calendar.</summary>
    public async Task DisconnectAsync(Guid tenantId, Guid actorMemberId, CancellationToken ct)
    {
        var connection = await sql.Queryable<CalendarConnection>().FirstAsync(c => c.TenantId == tenantId, ct);
        if (connection is null)
        {
            return;
        }

        await DeleteQuietlyAsync(connection.CalendarId, CalendarVerification.EventId(tenantId), ct);
        using var tran = sql.Ado.UseTran();
        await sql.Deleteable<CalendarConnection>().Where(c => c.Id == connection.Id && c.TenantId == tenantId).ExecuteCommandAsync(ct);
        await audit.WriteAsync(tenantId, actorMemberId, "calendar.disconnected", "Stopped copying bookings to the Google Calendar.", "calendar", connection.Id, ct);
        tran.CommitTran();
    }

    private async Task WriteCodeAsync(string calendarId, string eventId, string code, CancellationToken ct)
    {
        var start = DateTimeOffset.UtcNow;
        var body = new Event
        {
            Id = eventId,
            Status = "confirmed",
            Summary = CalendarVerification.SummaryPrefix + code,
            Description = "Type this code in the organization settings to connect this calendar. It stops working after a short while.",
            Start = new EventDateTime { DateTimeDateTimeOffset = start, TimeZone = "UTC" },
            End = new EventDateTime { DateTimeDateTimeOffset = start.AddMinutes(30), TimeZone = "UTC" },
        };

        try
        {
            await calendar.Events.Update(body, calendarId, eventId).ExecuteAsync(ct);
            return;
        }
        catch (GoogleApiException e) when (e.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) { }

        try
        {
            await calendar.Events.Insert(body, calendarId).ExecuteAsync(ct);
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.Conflict)
        {
            await calendar.Events.Update(body, calendarId, eventId).ExecuteAsync(ct);
        }
    }

    private async Task DeleteQuietlyAsync(string calendarId, string eventId, CancellationToken ct)
    {
        try
        {
            await calendar.Events.Delete(calendarId, eventId).ExecuteAsync(ct);
        }
        catch (GoogleApiException e) when (e.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) { }
        catch (GoogleApiException e)
        {
            logger.LogWarning(e, "The check event on calendar {CalendarId} could not be removed", calendarId);
        }
    }

    /// <summary>The address an admin shares the calendar with. Missing when the credential is not a service account.</summary>
    public string? ServiceAccountEmail()
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(google.Value.ServiceAccountJsonCredential));
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("client_email", out var email) && email.ValueKind == JsonValueKind.String
                ? email.GetString()
                : null;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    private static bool IsCalendarId(string calendarId) =>
        calendarId.Length is >= 1 and <= 256
        && calendarId.All(c => !char.IsWhiteSpace(c) && c is not '/' and not '?' and not '#')
        && !calendarId.Equals("primary", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Google refusal that will not change on another try. Rate limits and errors on Google's side are left out.</summary>
    private static bool IsPermanent(GoogleApiException e)
    {
        var reason = e.Error?.Errors?.FirstOrDefault()?.Reason;
        return e.HttpStatusCode switch
        {
            HttpStatusCode.Unauthorized => true,
            HttpStatusCode.NotFound => true,
            HttpStatusCode.Forbidden => reason is not ("rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "dailyLimitExceeded"),
            _ => false,
        };
    }

    private static CalendarAttempt Refuse(int status, string message, string code, string? field = null) =>
        new(null, new CalendarRefusal(status, message, code, field));
}