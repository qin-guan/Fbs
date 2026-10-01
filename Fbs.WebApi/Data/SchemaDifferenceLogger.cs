namespace Fbs.WebApi.Data;

/// <remarks>Adapted from GeeksHacking/portal.</remarks>
public static class SchemaDifferenceLogger
{
    public static void Write(
        ILogger logger,
        SchemaDifferenceReport report,
        LogLevel differenceLevel = LogLevel.Information
    )
    {
        if (!report.HasDifferences)
        {
            logger.LogInformation("No pending database schema changes.");
            return;
        }

        logger.Log(
            differenceLevel,
            "Pending database schema changes detected across {TableCount} table(s) and {IndexCount} missing index(es).",
            report.Tables.Count,
            report.MissingIndexes.Count
        );

        foreach (var index in report.MissingIndexes)
        {
            logger.Log(
                differenceLevel,
                "{TableName}: missing {Kind} index {IndexName}",
                index.TableName,
                index.IsUnique ? "unique" : "non-unique",
                index.IndexName
            );
        }

        foreach (var table in report.Tables)
        {
            logger.Log(
                differenceLevel,
                "{TableName}: +{AddedColumns} ~{UpdatedColumns} -{DeletedColumns} remarks:{UpdatedRemarks}",
                table.TableName,
                table.AddColumns.Count,
                table.UpdateColumns.Count,
                table.DeleteColumns.Count,
                table.UpdateRemarks.Count
            );

            foreach (var message in table.Differences.Select(column => column.Message).Where(m => !string.IsNullOrWhiteSpace(m)))
            {
                logger.Log(differenceLevel, "  {Message}", message);
            }
        }

        if (!string.IsNullOrWhiteSpace(report.RawText))
        {
            logger.LogDebug("Raw SqlSugar schema diff:{NewLine}{SchemaDiff}", Environment.NewLine, report.RawText);
        }
    }
}
