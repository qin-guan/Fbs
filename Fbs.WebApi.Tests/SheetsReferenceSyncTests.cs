using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Legacy;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Repository.Database;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Keeping users, facilities and the roster in step with the sheets after bookings are in the database, with
/// the real legacy stores reading a fake Google, and TiDB.
/// </summary>
public class SheetsReferenceSyncTests
{
    private sealed class Setup(FakeGoogle google, SqlSugarScope db, string slug)
    {
        public FakeGoogle Google { get; } = google;

        public SqlSugarScope Db { get; } = db;

        public string Slug { get; } = slug;

        public Guid TenantId => Db.Queryable<Tenant>().Single(t => t.Slug == Slug).Id;

        public DefaultTenant DefaultTenant() =>
            new(Db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:TenantSlug"] = Slug }).Build());

        public async Task ImportAsync()
        {
            await using var provider = LegacyGoogle.Services(Google);
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            await new LegacyImporter(
                Db,
                services.GetRequiredService<IUserRepository>(),
                services.GetRequiredService<IFacilityRepository>(),
                services.GetRequiredService<INominalRollRepository>(),
                services.GetRequiredService<IBookingService>()
            ).ImportAsync(new LegacyImportOptions { Slug = Slug });
        }

        public async Task<SheetsReferenceSyncReport> SyncAsync(Action<SheetsSyncOptions>? configure = null)
        {
            await using var provider = LegacyGoogle.Services(Google);
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var options = new SheetsSyncOptions();
            configure?.Invoke(options);
            return await new SheetsReferenceSync(
                Db,
                ActivatorUtilities.CreateInstance<UserRepository>(services),
                ActivatorUtilities.CreateInstance<FacilityRepository>(services),
                ActivatorUtilities.CreateInstance<NominalRollRepository>(services),
                DefaultTenant(),
                services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>(),
                Microsoft.Extensions.Options.Options.Create(options)
            ).SyncAsync();
        }

        public TenantMember Member(string phone)
        {
            var tenantId = TenantId;
            var stored = "+" + phone;
            return Db.Queryable<TenantMember>().Single(m => m.TenantId == tenantId && m.Phone == stored);
        }

        public string? UnitOf(TenantMember member) => member.UnitId is { } id ? Db.Queryable<Fbs.WebApi.Data.Entities.Unit>().Single(u => u.Id == id).Name : null;

        public List<DataFacility> Facilities()
        {
            var tenantId = TenantId;
            return Db.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToList();
        }

        public List<RosterEntry> Roster()
        {
            var tenantId = TenantId;
            return Db.Queryable<RosterEntry>().Where(r => r.TenantId == tenantId).ToList();
        }
    }

    private static async Task<Setup> SetUpAsync(Action<FakeGoogle>? shape = null)
    {
        var google = new FakeGoogle();
        shape?.Invoke(google);
        google.Sheets["Nominal Roll"] =
        [
            ["Name", "Unit", "Phone"],
            ["PTE Roster One", "Alpha", "6590000001"],
            ["PTE Roster Two", "Bravo", "6590000002"],
        ];
        var db = (await TestDatabase.SharedAsync()).CreateClient();
        var setup = new Setup(google, db, $"sync{Guid.NewGuid():N}"[..14]);
        await setup.ImportAsync();
        return setup;
    }

    [Test]
    public async Task Nothing_changes_when_the_sheets_have_not()
    {
        var setup = await SetUpAsync();

        var report = await setup.SyncAsync();

        await Assert.That(report.Changed).IsFalse();
        await Assert.That(report.Warnings).IsEmpty();
        await Assert.That(report.Members.Unchanged).IsEqualTo(5);
        await Assert.That(report.Facilities.Unchanged).IsEqualTo(3);
        await Assert.That(report.Roster.Unchanged).IsEqualTo(2);
    }

    [Test]
    public async Task People_facilities_and_roster_entries_added_to_the_sheets_are_added()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Users"].Add(["Delta", "PTE New", "6590001111", "4242", "All", "TRUE"]);
        setup.Google.Sheets["Facilities"].Add(["Range", "Outdoor", "Delta, Alpha"]);
        setup.Google.Sheets["Nominal Roll"].Add(["PTE Roster Three", "Delta", "6590000003"]);

        var report = await setup.SyncAsync();

        await Assert.That(report.Members.Added).IsEqualTo(1);
        await Assert.That(report.Facilities.Added).IsEqualTo(1);
        await Assert.That(report.Roster.Added).IsEqualTo(1);
        await Assert.That(report.Units.Added).IsEqualTo(1);
        var member = setup.Member("6590001111");
        await Assert.That(member.Status).IsEqualTo(MemberStatus.Unclaimed);
        await Assert.That(member.DisplayName).IsEqualTo("PTE New");
        await Assert.That(setup.UnitOf(member)).IsEqualTo("Delta");
        await Assert.That(member.LegacyChatId).IsEqualTo("4242");
        await Assert.That(member.Role).IsEqualTo(MemberRole.Admin);
        await Assert.That(member.NotificationScope).IsEqualTo(NotificationScope.All);

        var facilities = await new DatabaseFacilityRepository(setup.Db, setup.DefaultTenant()).GetListAsync();
        await Assert.That(facilities.Single(f => f.Name == "Range").Scope).IsEquivalentTo(["Alpha", "Delta"]);
        await Assert.That(setup.Roster().Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_new_name_and_unit_and_a_new_scope_and_group_are_picked_up()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Users"][1][0] = "Bravo";
        setup.Google.Sheets["Users"][1][1] = "CPT Renamed";
        setup.Google.Sheets["Facilities"][2][1] = "Fitness";
        setup.Google.Sheets["Facilities"][2][2] = "Charlie";
        setup.Google.Sheets["Nominal Roll"][1][0] = "PTE Renamed";

        var report = await setup.SyncAsync();

        await Assert.That(report.Members.Updated).IsEqualTo(1);
        await Assert.That(report.Facilities.Updated).IsEqualTo(1);
        await Assert.That(report.Roster.Updated).IsEqualTo(1);
        var member = setup.Member(Users.Booker);
        await Assert.That(member.DisplayName).IsEqualTo("CPT Renamed");
        await Assert.That(setup.UnitOf(member)).IsEqualTo("Bravo");
        var facilities = await new DatabaseFacilityRepository(setup.Db, setup.DefaultTenant()).GetListAsync();
        var field = facilities.Single(f => f.Name == "Field");
        await Assert.That(field.Group).IsEqualTo("Fitness");
        await Assert.That(field.Scope).IsEquivalentTo(["Charlie"]);
        await Assert.That(setup.Roster().Select(r => r.Name)).Contains("PTE Renamed");
    }

    [Test]
    public async Task What_is_changed_in_the_app_is_not_taken_back_by_the_sheet()
    {
        var setup = await SetUpAsync();
        // The bot links a chat and someone is made an admin, in the database, and the sheet still says what it did
        var repository = new DatabaseUserRepository(setup.Db, setup.DefaultTenant());
        var user = await repository.GetAsync(u => u.Phone == Users.Booker);
        user.TelegramChatId = "9999";
        user.IsAdmin = true;
        user.NotificationGroup = "All";
        await repository.UpdateAsync(user);

        var report = await setup.SyncAsync();

        await Assert.That(report.Changed).IsFalse();
        var member = setup.Member(Users.Booker);
        await Assert.That(member.LegacyChatId).IsEqualTo("9999");
        await Assert.That(member.Role).IsEqualTo(MemberRole.Admin);
        await Assert.That(member.NotificationScope).IsEqualTo(NotificationScope.All);
    }

    [Test]
    public async Task Someone_taken_off_the_sheet_leaves_and_can_come_back_and_what_they_booked_is_kept()
    {
        var setup = await SetUpAsync();
        var member = setup.Member(Users.OtherUnit);
        var facilityId = setup.Facilities().Single(f => f.Name == "Eiger").Id;
        var tenantId = setup.TenantId;
        var bookingId = Guid.NewGuid();
        setup.Db.Insertable(new Fbs.WebApi.Data.Entities.Booking { Id = bookingId, TenantId = tenantId, FacilityId = facilityId, StartUtc = DateTimeOffset.UtcNow.AddDays(3), EndUtc = DateTimeOffset.UtcNow.AddDays(3).AddHours(2), Conduct = "Kept", BookedByMemberId = member.Id }).ExecuteCommand();
        var row = setup.Google.Sheets["Users"][4];
        setup.Google.Sheets["Users"].RemoveAt(4);

        var left = await setup.SyncAsync();

        await Assert.That(left.MembersRemoved).IsEqualTo(1);
        await Assert.That(setup.Member(Users.OtherUnit).Status).IsEqualTo(MemberStatus.Removed);
        await Assert.That(setup.Db.Queryable<Fbs.WebApi.Data.Entities.Booking>().Single(b => b.Id == bookingId).BookedByMemberId).IsEqualTo(member.Id);
        var users = await new DatabaseUserRepository(setup.Db, setup.DefaultTenant()).GetListAsync();
        await Assert.That(users.Select(u => u.Phone)).DoesNotContain(Users.OtherUnit);

        setup.Google.Sheets["Users"].Add(row);
        var back = await setup.SyncAsync();

        await Assert.That(back.MembersRestored).IsEqualTo(1);
        await Assert.That(setup.Member(Users.OtherUnit).Status).IsEqualTo(MemberStatus.Unclaimed);
        await Assert.That(setup.Member(Users.OtherUnit).Id).IsEqualTo(member.Id);
    }

    [Test]
    public async Task Someone_who_has_an_account_comes_back_as_an_active_member()
    {
        var setup = await SetUpAsync();
        var tenantId = setup.TenantId;
        var phone = "+" + Users.OtherUnit;
        setup.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { UserId = Guid.NewGuid(), Status = MemberStatus.Removed }).Where(m => m.TenantId == tenantId && m.Phone == phone).ExecuteCommand();

        await setup.SyncAsync();

        await Assert.That(setup.Member(Users.OtherUnit).Status).IsEqualTo(MemberStatus.Active);
    }

    [Test]
    public async Task A_facility_taken_off_the_sheet_can_no_longer_be_booked_but_keeps_its_bookings()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Facilities"].RemoveAt(2);

        var report = await setup.SyncAsync();

        await Assert.That(report.FacilitiesClosed).IsEqualTo(1);
        var facilities = await new DatabaseFacilityRepository(setup.Db, setup.DefaultTenant()).GetListAsync();
        await Assert.That(facilities.Single(f => f.Name == "Field").Scope).IsNull();
        await Assert.That(setup.Facilities().Any(f => f.Name == "Field")).IsTrue();

        // And it stays closed the next time
        await Assert.That((await setup.SyncAsync()).Changed).IsFalse();
    }

    [Test]
    public async Task Someone_taken_off_the_roster_is_removed()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Nominal Roll"].RemoveAt(2);

        var report = await setup.SyncAsync();

        await Assert.That(report.RosterRemoved).IsEqualTo(1);
        await Assert.That(setup.Roster().Select(r => r.Phone)).IsEquivalentTo(["+6590000001"]);
    }

    [Test]
    public async Task Empty_sheets_are_taken_to_be_a_mistake_and_remove_nothing()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Users"].RemoveRange(1, 5);
        setup.Google.Sheets["Facilities"].RemoveRange(1, 3);
        setup.Google.Sheets["Nominal Roll"].RemoveRange(1, 2);

        var report = await setup.SyncAsync();

        await Assert.That(report.MembersRemoved).IsEqualTo(0);
        await Assert.That(report.FacilitiesClosed).IsEqualTo(0);
        await Assert.That(report.RosterRemoved).IsEqualTo(0);
        await Assert.That(report.Warnings.Count).IsEqualTo(3);
        await Assert.That(setup.Member(Users.Booker).Status).IsEqualTo(MemberStatus.Unclaimed);
        await Assert.That(setup.Roster().Count).IsEqualTo(2);
    }

    [Test]
    public async Task An_empty_users_sheet_removes_nobody_even_from_a_small_tenant()
    {
        // Too few for taking most of them off to be noticed, so the sheet being empty has to be
        var setup = await SetUpAsync(google => google.Sheets["Users"].RemoveRange(3, 3));
        setup.Google.Sheets["Users"].RemoveRange(1, 2);

        var report = await setup.SyncAsync();

        await Assert.That(report.MembersRemoved).IsEqualTo(0);
        await Assert.That(report.Warnings).Contains(w => w.Contains("nobody on it"));
        await Assert.That(setup.Member(Users.Booker).Status).IsEqualTo(MemberStatus.Unclaimed);
    }

    [Test]
    public async Task Taking_most_people_off_at_once_is_taken_to_be_a_mistake()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Users"].RemoveRange(2, 4);

        var report = await setup.SyncAsync();

        await Assert.That(report.MembersRemoved).IsEqualTo(0);
        await Assert.That(report.Warnings).Contains(w => w.Contains("4 of 5 members"));
        await Assert.That(setup.Member(Users.SameUnit).Status).IsEqualTo(MemberStatus.Unclaimed);

        // Fewer is taken to be meant
        var meant = await SetUpAsync();
        meant.Google.Sheets["Users"].RemoveRange(4, 2);
        await Assert.That((await meant.SyncAsync()).MembersRemoved).IsEqualTo(2);
    }

    [Test]
    public async Task Rows_that_are_not_usable_are_left_out_and_said_so()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Users"].Add(["Alpha", "Twice", Users.Booker, "", "None", "FALSE"]);
        setup.Google.Sheets["Users"].Add(["Alpha", "No Phone", "", "", "None", "FALSE"]);

        var report = await setup.SyncAsync();

        await Assert.That(report.Warnings.Count).IsEqualTo(2);
        await Assert.That(report.Changed).IsFalse();
        await Assert.That(setup.Member(Users.Booker).DisplayName).IsEqualTo("CPT Booker");
    }

    [Test]
    public async Task A_failure_part_way_changes_nothing()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Users"].Add(["Alpha", "PTE Fine", "6590002222", "", "None", "FALSE"]);
        setup.Google.Sheets["Facilities"].Add([new string('f', 150), "Too long a name", "All"]);

        await Assert.That(async () => await setup.SyncAsync()).Throws<Exception>();

        var tenantId = setup.TenantId;
        var phone = "+6590002222";
        await Assert.That(setup.Db.Queryable<TenantMember>().Any(m => m.TenantId == tenantId && m.Phone == phone)).IsFalse();
    }
}
