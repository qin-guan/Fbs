using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Repository.Database;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// What the database-backed stores do that the API tests, which go through the same seeded people and
/// facilities as the Google ones, don't reach.
/// </summary>
public class DatabaseStoreTests
{
    private sealed class Tenancy
    {
        public required SqlSugarScope Db { get; init; }
        public required Guid TenantId { get; init; }
        public required string Slug { get; init; }

        public DefaultTenant Tenant =>
            new(Db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:TenantSlug"] = Slug }).Build());

        public Guid AddUnit(string name)
        {
            var id = Guid.NewGuid();
            Db.Insertable(new DataUnit { Id = id, TenantId = TenantId, Name = name }).ExecuteCommand();
            return id;
        }

        public Guid AddMember(string name, string? phone, Guid? unitId = null, MemberRole role = MemberRole.Member, MemberStatus status = MemberStatus.Active)
        {
            var id = Guid.NewGuid();
            Db.Insertable(
                    new TenantMember
                    {
                        Id = id,
                        TenantId = TenantId,
                        DisplayName = name,
                        Phone = phone,
                        UnitId = unitId,
                        Role = role,
                        Status = status,
                    }
                )
                .ExecuteCommand();
            return id;
        }

        public Guid AddFacility(string name, bool all = false, params Guid[] units)
        {
            var id = Guid.NewGuid();
            Db.Insertable(new DataFacility { Id = id, TenantId = TenantId, Name = name, Group = "Group", AvailableToAll = all }).ExecuteCommand();
            foreach (var unit in units)
            {
                Db.Insertable(new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = TenantId, FacilityId = id, UnitId = unit }).ExecuteCommand();
            }

            return id;
        }
    }

    private static async Task<Tenancy> NewTenantAsync()
    {
        var db = (await TestDatabase.SharedAsync()).CreateClient();
        var id = Guid.NewGuid();
        var slug = $"t{id:N}"[..12];
        db.Insertable(new Tenant { Id = id, Slug = slug, Name = "Test" }).ExecuteCommand();
        return new Tenancy
        {
            Db = db,
            TenantId = id,
            Slug = slug,
        };
    }

    [Test]
    public async Task A_facility_nobody_can_book_has_no_scope_rather_than_an_empty_one()
    {
        var tenancy = await NewTenantAsync();
        tenancy.AddFacility("Nobody");

        var facilities = await new DatabaseFacilityRepository(tenancy.Db, tenancy.Tenant).GetListAsync();

        await Assert.That(facilities.Single().Scope).IsNull();
    }

    [Test]
    public async Task A_facility_open_to_everyone_is_scoped_to_All()
    {
        var tenancy = await NewTenantAsync();
        tenancy.AddFacility("Everyone", all: true);

        var facilities = await new DatabaseFacilityRepository(tenancy.Db, tenancy.Tenant).GetListAsync();

        await Assert.That(facilities.Single().Scope).IsEquivalentTo(["All"]);
    }

    [Test]
    public async Task A_facility_is_scoped_to_the_names_of_the_units_that_can_book_it()
    {
        var tenancy = await NewTenantAsync();
        var bravo = tenancy.AddUnit("Bravo");
        var alpha = tenancy.AddUnit("Alpha");
        tenancy.AddUnit("Charlie");
        tenancy.AddFacility("Some", all: false, bravo, alpha);

        var facilities = await new DatabaseFacilityRepository(tenancy.Db, tenancy.Tenant).GetListAsync();

        await Assert.That(facilities.Single().Scope).IsEquivalentTo(["Alpha", "Bravo"]);
    }

    [Test]
    public async Task Facilities_of_another_tenant_are_not_seen()
    {
        var mine = await NewTenantAsync();
        var theirs = await NewTenantAsync();
        mine.AddFacility("Mine", all: true);
        theirs.AddFacility("Theirs", all: true);

        var facilities = await new DatabaseFacilityRepository(mine.Db, mine.Tenant).GetListAsync();

        await Assert.That(facilities.Select(f => f.Name).OfType<string>()).IsEquivalentTo(["Mine"]);
    }

    [Test]
    public async Task A_member_is_a_user_with_their_unit_and_a_phone_number_the_way_the_api_shows_it()
    {
        var tenancy = await NewTenantAsync();
        var unit = tenancy.AddUnit("Alpha");
        tenancy.AddMember("CPT Booker", "+6591234567", unit, MemberRole.Admin);

        var user = (await new DatabaseUserRepository(tenancy.Db, tenancy.Tenant).GetListAsync()).Single();

        await Assert.That(user.Name).IsEqualTo("CPT Booker");
        await Assert.That(user.Unit).IsEqualTo("Alpha");
        await Assert.That(user.Phone).IsEqualTo("6591234567");
        await Assert.That(user.IsAdmin).IsTrue();
    }

    [Test]
    public async Task A_member_with_no_unit_or_phone_is_still_a_user()
    {
        var tenancy = await NewTenantAsync();
        tenancy.AddMember("Nobody", phone: null);

        var repository = new DatabaseUserRepository(tenancy.Db, tenancy.Tenant);
        var user = (await repository.GetListAsync()).Single();

        await Assert.That(user.Unit).IsNull();
        await Assert.That(user.Phone).IsNull();
        await Assert.That((await repository.GetByPhoneAsync()).Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_removed_member_is_no_longer_a_user()
    {
        var tenancy = await NewTenantAsync();
        tenancy.AddMember("Here", "+6591111111");
        tenancy.AddMember("Gone", "+6592222222", status: MemberStatus.Removed);

        var users = await new DatabaseUserRepository(tenancy.Db, tenancy.Tenant).GetListAsync();

        await Assert.That(users.Select(u => u.Name).OfType<string>()).IsEquivalentTo(["Here"]);
    }

    [Test]
    public async Task Updating_a_user_saves_their_chat_role_and_notifications_only()
    {
        var tenancy = await NewTenantAsync();
        tenancy.AddMember("CPT Booker", "+6591234567");
        var repository = new DatabaseUserRepository(tenancy.Db, tenancy.Tenant);

        var user = (await repository.GetListAsync()).Single();
        user.TelegramChatId = "4242";
        user.IsAdmin = true;
        user.NotificationGroup = "Unit";
        user.Name = "Not saved";
        await repository.UpdateAsync(user);

        var saved = (await repository.GetListAsync()).Single();
        await Assert.That(saved.TelegramChatId).IsEqualTo("4242");
        await Assert.That(saved.IsAdmin).IsTrue();
        await Assert.That(saved.NotificationGroup).IsEqualTo("Unit");
        await Assert.That(saved.Name).IsEqualTo("CPT Booker");
    }

    [Test]
    public async Task Updating_someone_who_is_not_a_member_fails()
    {
        var tenancy = await NewTenantAsync();
        var repository = new DatabaseUserRepository(tenancy.Db, tenancy.Tenant);

        await Assert
            .That(async () => await repository.UpdateAsync(new Fbs.WebApi.Entities.User { Name = "Stranger", Phone = "6590000000" }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task The_roster_has_phone_numbers_the_way_the_api_shows_them()
    {
        var tenancy = await NewTenantAsync();
        tenancy.Db.Insertable(new RosterEntry { Id = Guid.NewGuid(), TenantId = tenancy.TenantId, Name = "PTE Roster", Unit = "Alpha", Phone = "+6598765432" }).ExecuteCommand();

        var entry = (await new DatabaseNominalRollRepository(tenancy.Db, tenancy.Tenant).GetListAsync()).Single();

        await Assert.That(entry.Name).IsEqualTo("PTE Roster");
        await Assert.That(entry.Unit).IsEqualTo("Alpha");
        await Assert.That(entry.Phone).IsEqualTo("6598765432");
    }

    [Test]
    public async Task A_tenant_that_does_not_exist_says_how_to_fix_it()
    {
        var db = (await TestDatabase.SharedAsync()).CreateClient();
        var tenant = new DefaultTenant(
            db,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:TenantSlug"] = "no-such-tenant" }).Build()
        );

        var exception = await Assert.That(async () => await tenant.GetIdAsync()).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("no-such-tenant");
        await Assert.That(exception.Message).Contains("Storage:TenantSlug");
    }
}
