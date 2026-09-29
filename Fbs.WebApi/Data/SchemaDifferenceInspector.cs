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
            schemaDifferences
        );
    }
}

public sealed record SchemaDifferenceReport(
    IReadOnlyList<Type> EntityTypes,
    string RawText,
    IReadOnlyList<SchemaDifferenceTable> Tables
)
{
    public bool HasDifferences => Tables.Count > 0;

    /// <summary>Whether applying the changes would drop a column, and the data in it.</summary>
    public bool HasDestructiveChanges => Tables.Any(table => table.DeleteColumns.Count > 0);
}

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
