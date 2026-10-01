using ConsoleAppFramework;
using Fbs.WebApi.Data;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Fbs.DbMigrator.Commands;

public class ApplyCommand(ILogger<ApplyCommand> logger, ISqlSugarClient sql, DatabaseTarget target)
{
    /// <summary>
    /// Makes the tables match the entities. Stops before dropping columns unless told it may.
    /// </summary>
    /// <param name="allowDestructive">Allow dropping columns, and drop leftover schema diff tables.</param>
    /// <param name="createDatabase">Create the database first if it isn't there, for a fresh server.</param>
    /// <param name="cancellationToken"></param>
    [Command("apply")]
    public async Task Apply(
        bool allowDestructive = false,
        bool createDatabase = false,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation(
            "Applying pending database schema changes. AllowDestructive: {AllowDestructive}; CreateDatabase: {CreateDatabase}",
            allowDestructive,
            createDatabase
        );

        if (createDatabase)
        {
            await target.CreateIfMissingAsync(cancellationToken);
            logger.LogInformation("Database {Database} is there.", target.Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SchemaDiffTempTableCleaner.CleanUp(sql, logger, allowDestructive);

        var report = SchemaDifferenceInspector.Inspect(sql);
        logger.LogInformation("Collected schema differences for {EntityCount} entities.", report.EntityTypes.Count);
        SchemaDifferenceLogger.Write(logger, report);

        if (!report.HasDifferences)
        {
            return;
        }

        if (report.HasDestructiveChanges && !allowDestructive)
        {
            logger.LogWarning("Destructive database changes were detected and --allow-destructive was not supplied.");
            throw new InvalidOperationException(
                "Destructive database changes detected. Review the diff and rerun with --allow-destructive after approval."
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
        sql.CodeFirst.InitTables(report.EntityTypes.ToArray());
        logger.LogInformation("Database schema changes were applied.");

        // Applying should leave nothing to change. If it doesn't, say so now rather than at startup
        var after = SchemaDifferenceInspector.Inspect(sql);
        if (after.HasDifferences)
        {
            SchemaDifferenceLogger.Write(logger, after, LogLevel.Error);
            throw new InvalidOperationException("The schema still differs from the entities after it was applied.");
        }
    }
}
