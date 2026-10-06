using System.Diagnostics.Metrics;

namespace Fbs.WebApi.Telemetry;

/// <summary>
/// Counters and timers for running the service: whether it is up and fast, whether the database is keeping up,
/// whether outbound work arrives, whether people are using it, and why a sign-in was refused. OpenTelemetry in
/// <c>Fbs.ServiceDefaults</c> exports them when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, and the README says which to
/// watch. Requests, the runtime and the HTTP clients are counted by their own instrumentation.
/// </summary>
/// <remarks>
/// <para>
/// None of them is by organisation, so their number doesn't grow with the number of organisations. The tags have a few
/// values each, all of which are ours (a type of message, an outcome), never something a person typed.
/// </para>
/// <para>
/// They are static so anything can count without being handed a counter: a meter that nobody listens to costs a
/// check and no more.
/// </para>
/// </remarks>
public static class FbsMetrics
{
    public const string MeterName = "Fbs.WebApi";

    internal static readonly Meter Meter = new(MeterName, typeof(FbsMetrics).Assembly.GetName().Version?.ToString());

    /// <summary>Fast things (a statement, a send) are in milliseconds, and slow ones (Telegram, Google) in seconds.</summary>
    private static readonly InstrumentAdvice<double> SecondsAdvice = new() { HistogramBucketBoundaries = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30] };

    // The database

    /// <summary>A transaction the database rejected and that was run again, labelled by the reason. A large increase means transactions are contending for the same rows.</summary>
    public static readonly Counter<long> TransactionRetries = Meter.CreateCounter<long>("fbs.db.transaction.retries", "{retry}", "Transactions run again after the database turned them away");

    /// <summary>A transaction the database kept rejecting until the retries ran out. Above zero, a caller got an error.</summary>
    public static readonly Counter<long> TransactionsGivenUp = Meter.CreateCounter<long>("fbs.db.transaction.given_up", "{transaction}", "Transactions that failed after being run again as often as they are");

    public static readonly Histogram<double> QueryDuration = Meter.CreateHistogram("fbs.db.query.duration", "s", "How long a database statement took", tags: null, SecondsAdvice);

    public static readonly Counter<long> QueryErrors = Meter.CreateCounter<long>("fbs.db.query.errors", "{error}", "Database statements that failed");

    // Bookings

    public static readonly Counter<long> BookingsMade = Meter.CreateCounter<long>("fbs.bookings.made", "{booking}", "Bookings made, one for each slot");

    public static readonly Counter<long> BookingsChanged = Meter.CreateCounter<long>("fbs.bookings.changed", "{booking}", "Bookings changed");

    public static readonly Counter<long> BookingsCancelled = Meter.CreateCounter<long>("fbs.bookings.cancelled", "{booking}", "Bookings cancelled");

    /// <summary>Requests to book or move something that were refused because somebody else has it.</summary>
    public static readonly Counter<long> BookingClashes = Meter.CreateCounter<long>("fbs.bookings.clashes", "{request}", "Requests refused because the time is taken");

    /// <summary>An organisation past a limit on what it can hold or create, labelled by which limit.</summary>
    public static readonly Counter<long> QuotaRefusals = Meter.CreateCounter<long>("fbs.quota.refusals", "{request}", "Requests refused because the organisation is at a limit");

    // Outbound

    /// <summary>How the outbox handled a message, by type and outcome: <c>done</c>, <c>retry</c>, <c>dead</c> or <c>skipped</c>.</summary>
    public static readonly Counter<long> OutboxMessages = Meter.CreateCounter<long>("fbs.outbox.messages", "{message}", "Outbox messages handled, by type and outcome");

    public static readonly Histogram<double> OutboxHandleDuration = Meter.CreateHistogram("fbs.outbox.handle.duration", "s", "How long an outbox message took to handle", tags: null, SecondsAdvice);

    /// <summary>Telegram messages, one for each person: <c>sent</c>, <c>blocked</c> (they stopped the bot, or the chat is gone) or <c>failed</c>.</summary>
    public static readonly Counter<long> TelegramMessages = Meter.CreateCounter<long>("fbs.telegram.messages", "{message}", "Telegram messages, one for each person, by result");

    public static readonly Histogram<double> TelegramSendDuration = Meter.CreateHistogram("fbs.telegram.send.duration", "s", "How long Telegram took to take a message", tags: null, SecondsAdvice);

    /// <summary>An organisation's calendar that was refused by Google and so stopped being sent to, until somebody puts it right.</summary>
    public static readonly Counter<long> CalendarsFailed = Meter.CreateCounter<long>("fbs.calendar.connections.failed", "{connection}", "Calendars that Google refused, and were stopped");

    // Sign-in

    /// <summary>Session tokens that were refused, by reason: <c>expired</c>, <c>signature</c>, <c>unknown_key</c>, <c>issuer</c>, <c>azp</c>, <c>subject</c> or <c>invalid</c>.</summary>
    public static readonly Counter<long> AuthFailures = Meter.CreateCounter<long>("fbs.auth.failures", "{token}", "Session tokens refused, by why");

    /// <summary>Webhooks from Clerk, by type and result: <c>accepted</c>, <c>invalid_signature</c> or <c>not_configured</c>.</summary>
    public static readonly Counter<long> Webhooks = Meter.CreateCounter<long>("fbs.webhooks.received", "{webhook}", "Webhooks received, by type and result");

    public static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>("fbs.rate_limit.rejections", "{request}", "Requests refused for being too many, by which limit");

    // Usage

    public static readonly Counter<long> TenantsCreated = Meter.CreateCounter<long>("fbs.tenants.created", "{tenant}", "Organisations made");

    public static readonly Counter<long> AccountsCreated = Meter.CreateCounter<long>("fbs.accounts.created", "{account}", "Accounts made the first time somebody signed in");

    public static readonly Counter<long> AccountsErased = Meter.CreateCounter<long>("fbs.accounts.erased", "{account}", "Accounts erased because Clerk said they were deleted");

    /// <summary>
    /// Joining with a link, by outcome: <c>joined</c>, <c>waiting</c> (for an admin), <c>already_in</c>, <c>full</c> (the
    /// organisation is at its limit of people), <c>unusable</c> (ended, revoked or used up) or <c>not_found</c> (no such link; many of these are guessed).
    /// </summary>
    public static readonly Counter<long> InvitesUsed = Meter.CreateCounter<long>("fbs.invites.used", "{use}", "Invite links used, by outcome");

    public static readonly Counter<long> PlacesClaimed = Meter.CreateCounter<long>("fbs.claims.completed", "{claim}", "People carried over from before who took over their place");

    public static readonly Counter<long> TelegramLinked = Meter.CreateCounter<long>("fbs.telegram.linked", "{link}", "Telegram chats connected to an account");
}
