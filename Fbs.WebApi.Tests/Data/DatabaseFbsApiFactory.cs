using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using SqlSugar;
using Booking = Fbs.WebApi.Entities.Booking;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// The API with its database, seeded with an organisation of its own and the people (see <see cref="Users"/>) and
/// facilities the tests of the phone number sign-in book with.
/// </summary>
/// <remarks>
/// Every factory gets a tenant of its own in the shared test database, so tests don't see each other's data.
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

    /// <summary>Adds someone to the users, as if they were there before the test.</summary>
    public void AddUser(string unit, string name, string phone, string? telegramChatId, string notificationGroup, bool isAdmin = false)
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

    /// <summary>Adds a booking, as if it was made before the test.</summary>
    public Task AddBookingAsync(Booking booking)
    {
        AddBookings([booking]);
        return Task.CompletedTask;
    }

    /// <summary>Adds a lot of bookings before the test starts, quicker than one at a time.</summary>
    /// <remarks>Someone who booked and isn't a member, as with people who have since left, is kept as a member who has left.</remarks>
    public void AddBookings(IReadOnlyList<Booking> bookings)
    {
        var facilities = _db.Queryable<DataFacility>().Where(f => f.TenantId == _tenantId).ToList().ToDictionary(f => f.Name);
        var members = _db.Queryable<TenantMember>().Where(m => m.TenantId == _tenantId).ToList().ToDictionary(m => m.Phone!);
        var now = DateTimeOffset.UtcNow;

        var rows = new List<DataBooking>();
        foreach (var booking in bookings)
        {
            var phone = PhoneNumbers.ToStored(booking.UserPhone)!;
            if (!members.TryGetValue(phone, out var member))
            {
                member = new TenantMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    DisplayName = "Former member",
                    Phone = phone,
                    Status = MemberStatus.Removed,
                };
                _db.Insertable(member).ExecuteCommand();
                members[phone] = member;
            }

            rows.Add(
                new DataBooking
                {
                    Id = booking.Id == Guid.Empty ? Guid.NewGuid() : booking.Id,
                    TenantId = _tenantId,
                    FacilityId = facilities[booking.FacilityName!].Id,
                    StartUtc = booking.StartDateTime!.Value.ToUniversalTime(),
                    EndUtc = booking.EndDateTime!.Value.ToUniversalTime(),
                    Conduct = booking.Conduct ?? string.Empty,
                    Description = booking.Description,
                    PocName = booking.PocName,
                    PocPhone = booking.PocPhone,
                    BookedByMemberId = member.Id,
                    UnitId = member.UnitId,
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
        }

        _db.Insertable(rows).PageSize(500).ExecuteCommand();
    }

    /// <summary>How many bookings are stored, leaving out those that were cancelled.</summary>
    public int StoredBookingCount =>
        _db.Queryable<DataBooking>().Count(b => b.TenantId == _tenantId && b.CancelledAt == null);

    /// <summary>The ID of the member with the phone number, as they were seeded.</summary>
    public Guid MemberIdOf(string phone)
    {
        var stored = PhoneNumbers.ToStored(phone);
        return _db.Queryable<TenantMember>().First(m => m.TenantId == _tenantId && m.Phone == stored).Id;
    }

    /// <summary>The Telegram chat the user has linked, as stored.</summary>
    public string? TelegramChatIdOf(string phone)
    {
        var stored = PhoneNumbers.ToStored(phone);
        return _db.Queryable<TenantMember>().First(m => m.TenantId == _tenantId && m.Phone == stored).LegacyChatId;
    }

    /// <summary>
    /// Login codes are for a phone number, not a tenant, so ones left by an earlier test would still be
    /// there, and stop the next from asking for another.
    /// </summary>
    public void ClearLoginCodes() => _db.Deleteable<LoginOtp>().ExecuteCommand();

    /// <summary>How many login codes are stored.</summary>
    public int StoredCodeCount => _db.Queryable<LoginOtp>().Count();

    /// <summary>Makes the login code that was sent to the phone look like it was sent a while ago.</summary>
    public void AgeCode(string phone, TimeSpan age)
    {
        var stored = PhoneNumbers.ToStored(phone);
        var at = DateTimeOffset.UtcNow.Subtract(age);
        _db.Updateable<LoginOtp>().SetColumns(o => new LoginOtp { CreatedAt = at }).Where(o => o.Phone == stored).ExecuteCommand();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:db", _database.ConnectionString);
        builder.UseSetting("Storage:TenantSlug", Slug);
        // Every test's API shares the one outbox table, and only this one's messages are its to send
        builder.UseSetting("Outbox:TenantId", _tenantId.ToString());
        // Looks often, as messages written by another instance are only found that way
        builder.UseSetting("Outbox:PollInterval", "00:00:01");
        // Not looked at in the background, as what the gauges say is the same for every app, and the tests that read them look when they want to
        builder.UseSetting("Metrics:DatabaseGauges:Interval", "00:00:00");
    }
}
