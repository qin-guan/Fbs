using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Repository.Database;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// A tenant of its own in the shared test database, with the people and facilities a test adds to it, for
/// tests of the database stores that go straight to them rather than through the API.
/// </summary>
public sealed class TestTenant
{
    private TestTenant(SqlSugarScope db, Guid id, string slug)
    {
        Db = db;
        TenantId = id;
        Slug = slug;
    }

    /// <summary>For adding to and looking at what is stored. Use <see cref="NewClient"/> for anything being tested.</summary>
    public SqlSugarScope Db { get; }

    public Guid TenantId { get; }

    public string Slug { get; }

    public static async Task<TestTenant> CreateAsync(string timeZone = "Asia/Singapore")
    {
        var db = (await TestDatabase.SharedAsync()).CreateClient();
        var id = Guid.NewGuid();
        var slug = $"t{id:N}"[..12];
        db.Insertable(new Tenant { Id = id, Slug = slug, Name = "Test", TimeZone = timeZone }).ExecuteCommand();
        return new TestTenant(db, id, slug);
    }

    /// <summary>A client of the database the way the API has one, that hasn't been used for anything else.</summary>
    public async Task<SqlSugarScope> NewClientAsync() => (await TestDatabase.SharedAsync()).CreateClient();

    public DefaultTenant DefaultTenantFor(ISqlSugarClient db) =>
        new(db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:TenantSlug"] = Slug }).Build());

    public DefaultTenant Default => DefaultTenantFor(Db);

    public DatabaseBookingService BookingServiceFor(ISqlSugarClient db, OutboxSignal? signal = null) =>
        new(db, DefaultTenantFor(db), signal ?? new OutboxSignal());

    public Guid AddUnit(string name)
    {
        var id = Guid.NewGuid();
        Db.Insertable(new DataUnit { Id = id, TenantId = TenantId, Name = name }).ExecuteCommand();
        return id;
    }

    public Guid AddMember(
        string name,
        string? phone,
        Guid? unitId = null,
        MemberRole role = MemberRole.Member,
        MemberStatus status = MemberStatus.Active,
        string? telegramChatId = null,
        NotificationScope scope = NotificationScope.None
    )
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
                    LegacyChatId = telegramChatId,
                    NotificationScope = scope,
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
