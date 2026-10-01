using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace Fbs.WebApi.Tests;

public class SchemaTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

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
    public async Task Every_entity_is_found_and_the_ones_that_should_not_be_are_not()
    {
        var names = SchemaDifferenceInspector.GetEntityTypes().Select(t => t.Name).ToList();

        await Assert
            .That(names)
            .IsEquivalentTo(
                ["Booking", "BookingCalendarEvent", "CalendarConnection", "Facility", "FacilityUnitAccess", "LoginOtp", "OutboxMessage", "RosterEntry", "Tenant", "TenantMember", "Unit"]
            );
    }

    [Test]
    public async Task A_database_without_the_tables_is_reported_as_needing_all_of_them()
    {
        var (database, db) = await NewDatabaseAsync(applySchema: false);
        await using var _ = database;

        var report = SchemaDifferenceInspector.Inspect(db);

        await Assert.That(report.HasDifferences).IsTrue();
        await Assert.That(report.Tables.Count).IsEqualTo(SchemaDifferenceInspector.GetEntityTypes().Length);
        await Assert.That(report.HasDestructiveChanges).IsFalse();
    }

    [Test]
    public async Task Applying_the_schema_leaves_nothing_to_change_and_no_temporary_tables()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;

        var report = SchemaDifferenceInspector.Inspect(db);

        await Assert.That(report.HasDifferences).IsFalse();
        await Assert.That(SchemaDiffTempTableCleaner.CleanUp(db, NullLogger.Instance, allowDestructive: false)).IsEmpty();
    }

    [Test]
    public async Task A_column_that_is_missing_is_added_without_being_destructive()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        await db.Ado.ExecuteCommandAsync("ALTER TABLE RosterEntry DROP COLUMN Unit");

        var report = SchemaDifferenceInspector.Inspect(db);

        var table = await Assert.That(report.Tables).HasSingleItem();
        await Assert.That(table.TableName).IsEqualTo("RosterEntry");
        await Assert.That(table.AddColumns.Single().Message).Contains("Unit");
        await Assert.That(report.HasDestructiveChanges).IsFalse();
    }

    [Test]
    public async Task A_column_that_is_no_longer_in_the_entity_is_a_destructive_change()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        await db.Ado.ExecuteCommandAsync("ALTER TABLE Facility ADD COLUMN Retired INT NULL");

        var report = SchemaDifferenceInspector.Inspect(db);

        await Assert.That(report.HasDestructiveChanges).IsTrue();
        await Assert.That(report.Tables.Single().DeleteColumns.Single().Message).Contains("Retired");
    }

    [Test]
    public async Task A_dropped_index_is_a_difference_and_putting_it_back_is_not_destructive()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        await db.Ado.ExecuteCommandAsync("DROP INDEX UX_Facility_TenantId_Name ON Facility");

        var report = SchemaDifferenceInspector.Inspect(db);

        await Assert.That(report.HasDifferences).IsTrue();
        await Assert.That(report.HasDestructiveChanges).IsFalse();
        var missing = await Assert.That(report.MissingIndexes).HasSingleItem();
        await Assert.That(missing).IsEqualTo(new MissingIndex("Facility", "UX_Facility_TenantId_Name", IsUnique: true));

        db.CodeFirst.InitTables(report.EntityTypes.ToArray());

        await Assert.That(SchemaDifferenceInspector.Inspect(db).HasDifferences).IsFalse();
    }

    [Test]
    public async Task Leftover_temporary_tables_are_only_dropped_when_allowed()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        await db.Ado.ExecuteCommandAsync("CREATE TABLE TempDiff260929123456789 (Id INT)");

        var found = SchemaDiffTempTableCleaner.CleanUp(db, NullLogger.Instance, allowDestructive: false);
        var stillThere = db.DbMaintenance.IsAnyTable("TempDiff260929123456789", false);
        SchemaDiffTempTableCleaner.CleanUp(db, NullLogger.Instance, allowDestructive: true);

        await Assert.That(found).IsEquivalentTo(["TempDiff260929123456789"]);
        await Assert.That(stillThere).IsTrue();
        await Assert.That(db.DbMaintenance.IsAnyTable("TempDiff260929123456789", false)).IsFalse();
    }

    [Test]
    public async Task Two_facilities_in_a_tenant_cannot_share_a_name_but_two_tenants_can()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        Facility Field(Guid tenantId) => new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Field" };
        await db.Insertable(Field(TenantId)).ExecuteCommandAsync();

        await Assert.That(async () => await db.Insertable(Field(TenantId)).ExecuteCommandAsync()).Throws<Exception>();
        await db.Insertable(Field(Guid.NewGuid())).ExecuteCommandAsync();

        await Assert.That(await db.Queryable<Facility>().CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task Members_without_a_phone_number_do_not_clash_but_the_same_phone_does()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        TenantMember Member(string? phone) => new() { Id = Guid.NewGuid(), TenantId = TenantId, DisplayName = "M", Phone = phone };

        await db.Insertable(Member(null)).ExecuteCommandAsync();
        await db.Insertable(Member(null)).ExecuteCommandAsync();
        await db.Insertable(Member("+6591234567")).ExecuteCommandAsync();

        await Assert.That(async () => await db.Insertable(Member("+6591234567")).ExecuteCommandAsync()).Throws<Exception>();
    }

    [Test]
    public async Task A_booking_keeps_everything_it_was_made_with()
    {
        var (database, db) = await NewDatabaseAsync();
        await using var _ = database;
        var start = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(8));
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            FacilityId = Guid.NewGuid(),
            StartUtc = start,
            EndUtc = start.AddHours(2),
            Conduct = "Section training",
            Description = "Bring water",
            PocName = "3SG Tan",
            PocPhone = "+6598765432",
            BookedByMemberId = Guid.NewGuid(),
            UnitId = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
        };

        await db.Insertable(booking).ExecuteCommandAsync();
        var stored = await db.Queryable<Booking>().FirstAsync(b => b.Id == booking.Id);

        await Assert.That(stored.StartUtc).IsEqualTo(start);
        await Assert.That(stored.StartUtc.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(stored.EndUtc).IsEqualTo(start.AddHours(2));
        await Assert.That(stored.PocName).IsEqualTo("3SG Tan");
        await Assert.That(stored.PocPhone).IsEqualTo("+6598765432");
        await Assert.That(stored.BookedByMemberId).IsEqualTo(booking.BookedByMemberId);
        await Assert.That(stored.UpdatedByMemberId).IsNull();
        await Assert.That(stored.CancelledAt).IsNull();
        await Assert.That(stored.Revision).IsEqualTo(1);
    }
}
