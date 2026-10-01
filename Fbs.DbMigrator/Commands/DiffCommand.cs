using ConsoleAppFramework;
using Fbs.WebApi.Data;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Fbs.DbMigrator.Commands;

public class DiffCommand(ILogger<DiffCommand> logger, ISqlSugarClient sql)
{
    /// <summary>
    /// Shows what applying would change, without changing anything. Exits with 2 when there is
    /// something to change, so a pipeline can tell.
    /// </summary>
    [Command("diff")]
    public Task<int> Diff(CancellationToken cancellationToken)
    {
        logger.LogInformation("Inspecting pending database schema differences.");
        cancellationToken.ThrowIfCancellationRequested();

        SchemaDiffTempTableCleaner.CleanUp(sql, logger, allowDestructive: false);
        var report = SchemaDifferenceInspector.Inspect(sql);
        logger.LogInformation("Collected schema differences for {EntityCount} entities.", report.EntityTypes.Count);

        SchemaDifferenceLogger.Write(logger, report);

        return Task.FromResult(report.HasDifferences ? 2 : 0);
    }
}
