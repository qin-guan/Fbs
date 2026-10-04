using System.Diagnostics;
using Fbs.WebApi.Data;
using OpenTelemetry;

namespace Fbs.WebApi.Telemetry;

/// <summary>
/// What the database driver makes of each statement, kept to what it did and how long it took, and never the SQL. A batch of
/// inserts has some of its values in the statement itself and not in parameters (the ids and times of what it inserts, and
/// nothing says that what people typed will always be kept out of it), and the SQL is not for wherever the traces are sent. The
/// kind of statement is left in <c>db.operation</c>, as <see cref="SqlSugarClientFactory.OperationOf"/> says it for the metrics.
/// </summary>
/// <remarks>
/// It has to come before the exporter, as a span is on its way as soon as the exporter has it, and it does:
/// <c>UseOtlpExporter</c> puts the exporter last, whatever the order they are added in.
/// </remarks>
public sealed class DatabaseSpans : BaseProcessor<Activity>
{
    /// <summary>The name of the driver's source of spans, and of its meter.</summary>
    public const string DriverName = "MySqlConnector";

    // Both what the driver says by default, and what the newer conventions have it say
    private static readonly string[] StatementTags = ["db.statement", "db.query.text"];

    public override void OnEnd(Activity data)
    {
        if (data.Source.Name != DriverName)
        {
            return;
        }

        string? statement = null;
        foreach (var tag in StatementTags)
        {
            statement ??= data.GetTagItem(tag) as string;
            data.SetTag(tag, null);
        }

        if (statement is not null)
        {
            data.SetTag("db.operation", SqlSugarClientFactory.OperationOf(statement));
        }
    }
}
