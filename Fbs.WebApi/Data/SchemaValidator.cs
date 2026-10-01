using System.Reflection;
using SqlSugar;

namespace Fbs.WebApi.Data;

/// <summary>
/// Checks, without changing anything, that the database has what the code needs: every table, every
/// column and every declared index.
/// </summary>
/// <remarks>
/// This is what the API runs at startup, so a version whose schema hasn't been applied yet fails then
/// instead of on the first request that needs it. <see cref="SchemaDifferenceInspector"/> compares more
/// closely, but it creates temporary tables to do it, which the API's database user shouldn't have to be
/// allowed to. Extra columns and indexes are fine, so the previous version keeps working against a
/// database that the next one has already been applied to.
/// </remarks>
public static class SchemaValidator
{
    public static IReadOnlyList<string> FindProblems(ISqlSugarClient sql)
    {
        var problems = new List<string>();
        foreach (var type in SchemaDifferenceInspector.GetEntityTypes())
        {
            var entity = sql.EntityMaintenance.GetEntityInfo(type);
            var table = entity.DbTableName;
            if (!sql.DbMaintenance.IsAnyTable(table, false))
            {
                problems.Add($"Table {table} is missing");
                continue;
            }

            var columns = sql.DbMaintenance
                .GetColumnInfosByTableName(table, false)
                .Select(column => column.DbColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            problems.AddRange(
                entity
                    .Columns.Where(column => !column.IsIgnore && !columns.Contains(column.DbColumnName))
                    .Select(column => $"Column {table}.{column.DbColumnName} is missing")
            );

            var indexes = sql.DbMaintenance.GetIndexList(table).ToHashSet(StringComparer.OrdinalIgnoreCase);
            problems.AddRange(
                type.GetCustomAttributes<SugarIndexAttribute>()
                    .Where(index => !indexes.Contains(index.IndexName))
                    .Select(index => $"Index {table}.{index.IndexName} is missing")
            );
        }

        return problems;
    }
}
