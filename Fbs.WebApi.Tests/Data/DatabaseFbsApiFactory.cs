using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// The API with its users, facilities, roster and login codes in the database rather than in Google
/// Sheets, seeded with the same people and facilities that <see cref="Fakes.FakeGoogle"/> starts with.
/// </summary>
/// <remarks>
/// Bookings are still in Google Calendar. Every factory gets a tenant of its own in the shared test
/// database, so tests don't see each other's data.
/// </remarks>
public class DatabaseFbsApiFactory : FbsApiFactory
{
    private readonly TestDatabase _database;
    private readonly SqlSugarScope _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Dictionary<string, Guid> _units = new();

    public DatabaseFbsApiFactory()
    {
        _database = TestDatabase.SharedAsync().GetAwaiter().GetResult();
        _db = _database.CreateClient();
        Slug = $"t{_tenantId:N}"[..12];
        Seed();
    }

    public string Slug { get; }

    public SqlSugarScope Db => _db;

    public Guid TenantId => _tenantId;

    private void Seed()
    {
        _db.Insertable(new Tenant { Id = _tenantId, Slug = Slug, Name = "Test" }).ExecuteCommand();
        foreach (var unit in new[] { "Alpha", "Bravo", "Charlie" })
        {
            var id = Guid.NewGuid();
            _units[unit] = id;
            _db.Insertable(new DataUnit { Id = id, TenantId = _tenantId, Name = unit }).ExecuteCommand();
        }

        AddUser("Alpha", "CPT Booker", Users.Booker, "1001", "Unit");
        AddUser("Alpha", "LTA Same Unit", Users.SameUnit, "1002", "Unit");
        AddUser("Bravo", "3SG Everyone", Users.AllGroup, "1003", "All");
        AddUser("Bravo", "PTE Other Unit", Users.OtherUnit, "1004", "Unit");
        AddUser("Charlie", "MAJ Admin", Users.Admin, "1005", "None", isAdmin: true);

        AddFacility("Eiger", "Parade Square", all: true);
        AddFacility("Field", "Sports", "Alpha", "Bravo");
        AddFacility("Gym", "Sports", "Bravo");
    }

    private void AddFacility(string name, string group, params string[] units) => AddFacility(name, group, all: false, units);

    private void AddFacility(string name, string group, bool all, params string[] units)
    {
        var id = Guid.NewGuid();
        _db.Insertable(new DataFacility { Id = id, TenantId = _tenantId, Name = name, Group = group, AvailableToAll = all }).ExecuteCommand();
        foreach (var unit in units)
        {
            _db.Insertable(new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = _tenantId, FacilityId = id, UnitId = _units[unit] }).ExecuteCommand();
        }
    }

    public override void AddUser(string unit, string name, string phone, string? telegramChatId, string notificationGroup, bool isAdmin = false)
    {
        if (!_units.TryGetValue(unit, out var unitId))
        {
            unitId = _units[unit] = Guid.NewGuid();
            _db.Insertable(new DataUnit { Id = unitId, TenantId = _tenantId, Name = unit }).ExecuteCommand();
        }

        _db.Insertable(
                new TenantMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    UnitId = unitId,
                    DisplayName = name,
                    Phone = PhoneNumbers.ToStored(phone),
                    LegacyChatId = string.IsNullOrEmpty(telegramChatId) ? null : telegramChatId,
                    NotificationScope = Enum.Parse<NotificationScope>(notificationGroup, ignoreCase: true),
                    Role = isAdmin ? MemberRole.Admin : MemberRole.Member,
                }
            )
            .ExecuteCommand();
    }

    public override string? TelegramChatIdOf(string phone)
    {
        var stored = PhoneNumbers.ToStored(phone);
        return _db.Queryable<TenantMember>().First(m => m.TenantId == _tenantId && m.Phone == stored).LegacyChatId;
    }

    /// <summary>
    /// Login codes are for a phone number, not a tenant, so ones left by an earlier test would still be
    /// there, and stop the next from asking for another.
    /// </summary>
    public void ClearLoginCodes() => _db.Deleteable<LoginOtp>().ExecuteCommand();

    public override int StoredCodeCount =>_db.Queryable<LoginOtp>().Count();

    public override void AgeCode(string phone, TimeSpan age)
    {
        var stored = PhoneNumbers.ToStored(phone);
        var at = DateTimeOffset.UtcNow.Subtract(age);
        _db.Updateable<LoginOtp>().SetColumns(o => new LoginOtp { CreatedAt = at }).Where(o => o.Phone == stored).ExecuteCommand();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Storage:Provider", "Database");
        builder.UseSetting("ConnectionStrings:db", _database.ConnectionString);
        builder.UseSetting("Storage:TenantSlug", Slug);
    }
}
