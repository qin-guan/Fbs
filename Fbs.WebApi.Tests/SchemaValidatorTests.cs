using Fbs.WebApi.Data;
using Fbs.WebApi.Tests.Data;
using SqlSugar;

namespace Fbs.WebApi.Tests;

public class SchemaValidatorTests
{
    private static async Task<(TestDatabase Database, SqlSugarScope Db)> NewDatabaseAsync(bool applySchema = true)
    {
        var database = await TestDatabase.CreateAsync();
        var db = database.CreateClient();
        if (applySchema)
        {
            db.CodeFirst.InitTables(SchemaDifferenceInspector.GetEntityTypes());
        }

        return (database, db);
    }

    [Test]
    public async Task An_empty_database_is_missing_every_table()
    {
        var (database, db) = await NewDatabaseAsync(applySchema: false);
        await using var _ = database;

        var problems = SchemaValidator.FindProblems(db);

        await Assert.That(problems.Count).IsEqualTo(SchemaDifferenceInspector.GetEntityTypes().Length);
        await Assert.That(problems).Contains("Table Booking is missing");
    }

    [Test]
    public async Task A_database_with_the_schema_applied_has_no_problems()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;

        await Assert.That(SchemaValidator.FindProblems(db)).IsEmpty();
    }

    [Test]
    public async Task A_missing_column_is_a_problem()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        await db.Ado.ExecuteCommandAsync("ALTER TABLE RosterEntry DROP COLUMN Unit");

        await Assert.That(SchemaValidator.FindProblems(db)).IsEquivalentTo(["Column RosterEntry.Unit is missing"]);
    }

    [Test]
    public async Task A_missing_index_is_a_problem()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        await db.Ado.ExecuteCommandAsync("DROP INDEX UX_Facility_TenantId_Name ON Facility");

        await Assert.That(SchemaValidator.FindProblems(db)).IsEquivalentTo(["Index Facility.UX_Facility_TenantId_Name is missing"]);
    }

    [Test]
    public async Task Extra_columns_and_indexes_are_fine_so_the_previous_version_keeps_working()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        await db.Ado.ExecuteCommandAsync("ALTER TABLE Facility ADD COLUMN Retired INT NULL");
        await db.Ado.ExecuteCommandAsync("CREATE INDEX IX_Facility_Retired ON Facility (Retired)");

        await Assert.That(SchemaValidator.FindProblems(db)).IsEmpty();
    }

    [Test]
    public async Task Checking_changes_nothing()
    {
        var (database, db) = await NewDatabaseAsync(applySchema: false);
        await using var _ = database;

        SchemaValidator.FindProblems(db);

        // No tables created, and none of the temporary ones a schema diff would leave
        await Assert.That(db.DbMaintenance.GetTableInfoList(false)).IsEmpty();
    }
}
