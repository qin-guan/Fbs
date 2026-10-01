using System.Reflection;
using System.Runtime.CompilerServices;
using SqlSugar;

namespace Fbs.WebApi.Data;

/// <summary>
/// Compares the tables in the database with the entities in <see cref="EntityNamespace"/>.
/// </summary>
/// <remarks>Adapted from GeeksHacking/portal.</remarks>
public static class SchemaDifferenceInspector
{
    public const string EntityNamespace = "Fbs.WebApi.Data.Entities";

    public static Type[] GetEntityTypes() =>
        typeof(SchemaDifferenceInspector)
            .Assembly.GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false, IsNested: false, IsGenericTypeDefinition: false }
                && !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), inherit: false)
                && string.Equals(type.Namespace, EntityNamespace, StringComparison.Ordinal)
            )
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

    public static SchemaDifferenceReport Inspect(ISqlSugarClient sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var entityTypes = GetEntityTypes();
        var differenceProvider = sql.CodeFirst.GetDifferenceTables(entityTypes);
        var schemaDifferences = differenceProvider
            .ToDiffList()
            .Where(table => table.IsDiff)
            .Select(SchemaDifferenceTable.From)
            .ToArray();

        return new SchemaDifferenceReport(
            entityTypes,
            differenceProvider.ToDiffString()?.Trim() ?? string.Empty,
            schemaDifferences,
            FindMissingIndexes(sql, entityTypes)
        );
    }

    /// <summary>
    /// The indexes the entities declare that a table that is there doesn't have. SqlSugar's own
    /// comparison only looks at columns, so a dropped unique index would go unnoticed, and with it
    /// what stops duplicates. Tables that aren't there yet are reported as whole tables instead.
    /// </summary>
    private static IReadOnlyList<MissingIndex> FindMissingIndexes(ISqlSugarClient sql, Type[] entityTypes)
    {
        var missing = new List<MissingIndex>();
        foreach (var type in entityTypes)
        {
            var declared = type.GetCustomAttributes<SugarIndexAttribute>().ToList();
            var table = sql.EntityMaintenance.GetEntityInfo(type).DbTableName;
            if (declared.Count == 0 || !sql.DbMaintenance.IsAnyTable(table, false))
            {
                continue;
            }

            var existing = sql.DbMaintenance.GetIndexList(table).ToHashSet(StringComparer.OrdinalIgnoreCase);
            missing.AddRange(
                declared
                    .Where(index => !existing.Contains(index.IndexName))
                    .Select(index => new MissingIndex(table, index.IndexName, index.IsUnique))
            );
        }

        return missing;
    }
}

public sealed record SchemaDifferenceReport(
    IReadOnlyList<Type> EntityTypes,
    string RawText,
    IReadOnlyList<SchemaDifferenceTable> Tables,
    IReadOnlyList<MissingIndex> MissingIndexes
)
{
    public bool HasDifferences => Tables.Count > 0 || MissingIndexes.Count > 0;

    /// <summary>Whether applying the changes would drop a column, and the data in it.</summary>
    public bool HasDestructiveChanges => Tables.Any(table => table.DeleteColumns.Count > 0);
}

public sealed record MissingIndex(string TableName, string IndexName, bool IsUnique);

public sealed record SchemaDifferenceTable(
    string TableName,
    IReadOnlyList<DiffColumsInfo> AddColumns,
    IReadOnlyList<DiffColumsInfo> UpdateColumns,
    IReadOnlyList<DiffColumsInfo> DeleteColumns,
    IReadOnlyList<DiffColumsInfo> UpdateRemarks
)
{
    public static SchemaDifferenceTable From(TableDifferenceInfo table) =>
        new(table.TableName, table.AddColums, table.UpdateColums, table.DeleteColums, table.UpdateRemark);

    public bool HasDifferences =>
        AddColumns.Count > 0 || UpdateColumns.Count > 0 || DeleteColumns.Count > 0 || UpdateRemarks.Count > 0;

    public IEnumerable<DiffColumsInfo> Differences =>
        AddColumns.Concat(UpdateColumns).Concat(DeleteColumns).Concat(UpdateRemarks);
}
