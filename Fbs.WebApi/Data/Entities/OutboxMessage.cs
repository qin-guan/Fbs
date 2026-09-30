using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// Something to do once a change has been saved, such as telling people about it. It is written in the
/// same transaction as the change, so it is never lost when the change is kept, or left behind when it
/// isn't, and is done afterwards, as often as it takes.
/// </summary>
/// <remarks>
/// TiDB has no <c>SKIP LOCKED</c>, so a dispatcher takes messages by leasing them: setting
/// <see cref="LockedBy"/> and <see cref="LockedUntil"/> in a single statement. A dispatcher that dies
/// simply lets its lease run out.
/// </remarks>
[SugarIndex("IX_OutboxMessage_Status_NextAttemptAt", nameof(Status), OrderByType.Asc, nameof(NextAttemptAt), OrderByType.Asc)]
public class OutboxMessage
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>What it is, which says what handles it, such as <c>telegram.booking</c>.</summary>
    [SugarColumn(Length = 64)]
    public string Type { get; set; } = null!;

    /// <summary>JSON, whose shape depends on <see cref="Type"/>.</summary>
    [SugarColumn(ColumnDataType = "text")]
    public string Payload { get; set; } = null!;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>How many times it has been taken, including one that was being done when the process died.</summary>
    public int Attempts { get; set; }

    /// <summary>Not before this, which is later after every failure.</summary>
    /// <remarks>
    /// The times here keep microseconds. Whole seconds would round, so a message written just before the half
    /// second would be dated a moment ahead and not be due when looked for, and messages written in the same
    /// second would have no order.
    /// </remarks>
    [SugarColumn(ColumnDataType = "datetime(6)")]
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Someone has it until then, and if they haven't finished by then anyone else may take it.</summary>
    [SugarColumn(ColumnDataType = "datetime(6)", IsNullable = true)]
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Who has it. Only they can say it is done or failed, so a dispatcher that was too slow can't undo the one that took over.</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? LockedBy { get; set; }

    [SugarColumn(ColumnDataType = "datetime(6)")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [SugarColumn(ColumnDataType = "datetime(6)", IsNullable = true)]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Why it last failed, or why it was given up on.</summary>
    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? LastError { get; set; }
}

public enum OutboxStatus
{
    Pending = 1,
    Done = 2,

    /// <summary>Given up on. It stays so someone can see what failed, and won't be tried again.</summary>
    Dead = 3,

    /// <summary>Not done, on purpose: the organisation it was for could not be used, so nobody was told. It is kept for a while like one that is done.</summary>
    Skipped = 4,
}
