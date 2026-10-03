using Fbs.WebApi.Telemetry;
using MySqlConnector;
using SqlSugar;

namespace Fbs.WebApi.Data;

/// <summary>
/// Creates the SqlSugar client used by the API and the database migrator.
/// </summary>
/// <remarks>
/// Adapted from GeeksHacking/portal. MySQL and TiDB <c>datetime</c> columns do not store an offset.
/// Out of the box SqlSugar writes the wall-clock part of whatever offset a <see cref="DateTimeOffset"/>
/// carries and reads values back in the server's local time zone, so the stored instant would depend
/// on the caller and on the <c>TZ</c> of the host. Every <see cref="DateTimeOffset"/> is therefore
/// converted to UTC before it is written, and every stored value is read back as UTC.
/// </remarks>
public static class SqlSugarClientFactory
{
    public static SqlSugarScope Create(string connectionString)
    {
        var config = new ConnectionConfig
        {
            DbType = DbType.MySql,
            ConnectionString = WithUtcDateTimeKind(connectionString),
            IsAutoCloseConnection = true,
            MoreSettings = new ConnMoreSettings { IsAutoRemoveDataCache = true },
        };

        return new SqlSugarScope(config, ConfigureUtcDateTimes);
    }

    /// <summary>
    /// Makes MySqlConnector return <c>datetime</c> values as <see cref="DateTimeKind.Utc"/>, which SqlSugar
    /// then materializes as <see cref="DateTimeOffset"/> values with a zero offset instead of the host's
    /// local offset.
    /// </summary>
    public static string WithUtcDateTimeKind(string connectionString)
    {
        return new MySqlConnectionStringBuilder(connectionString)
        {
            DateTimeKind = MySqlDateTimeKind.Utc,
        }.ConnectionString;
    }

    private static void ConfigureUtcDateTimes(SqlSugarClient db)
    {
        // Parameterized SQL, including values captured in Where/SetColumns expressions
        db.Aop.OnExecutingChangeSql = (sql, parameters) =>
        {
            foreach (var parameter in parameters ?? [])
            {
                parameter.Value = ToUtc(parameter.Value);
            }

            return new KeyValuePair<string, SugarParameter[]>(sql, parameters!);
        };

        // How long each statement takes, and which fail: the first thing to look at when it is slow
        db.Aop.OnLogExecuted = (sql, _) => FbsMetrics.QueryDuration.Record(db.Ado.SqlExecutionTime.TotalSeconds, new KeyValuePair<string, object?>("operation", OperationOf(sql)));
        db.Aop.OnError = exception => FbsMetrics.QueryErrors.Add(1, new KeyValuePair<string, object?>("operation", OperationOf(exception.Sql)));

        // Insertable/Updateable/Storageable entities. Batched statements inline their values into the SQL
        // text instead of using parameters, so the entity values themselves have to be normalized
        db.Aop.DataExecuting = (value, entityInfo) =>
        {
            if (value is DateTimeOffset { Offset.Ticks: not 0 } dateTimeOffset)
            {
                entityInfo.SetValue(dateTimeOffset.ToUniversalTime());
            }
        };
    }

    /// <summary>What a statement does, which is few enough values to tag by: never the statement, which has what people typed in it.</summary>
    internal static string OperationOf(string? sql)
    {
        var text = sql?.TrimStart() ?? string.Empty;
        var end = text.IndexOfAny([' ', '\n', '\r', '\t']);
        var word = (end < 0 ? text : text[..end]).ToLowerInvariant();
        return word is "select" or "insert" or "update" or "delete" or "begin" or "commit" or "rollback" ? word : "other";
    }

    private static object? ToUtc(object? value)
    {
        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
            DateTime { Kind: DateTimeKind.Local } dateTime => dateTime.ToUniversalTime(),
            _ => value,
        };
    }
}
