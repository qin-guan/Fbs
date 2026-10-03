using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Legacy;
using Fbs.WebApi.Options;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Repository.Database;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using ZiggyCreatures.Caching.Fusion;
using Booking = Fbs.WebApi.Entities.Booking;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Moving what is in Google Sheets and Calendar into the database, with the real legacy stores reading a
/// fake Google, and TiDB.
/// </summary>
public class LegacyImporterTests
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private static DateTimeOffset At(double hours, int days = 10)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(days).AddHours(hours);
    }

    private static Booking Legacy(string facility, double from, double to, string? phone = Users.Booker, int days = 10, string conduct = "Section training") =>
        new()
        {
            Id = Guid.NewGuid(),
            FacilityName = facility,
            StartDateTime = At(from, days),
            EndDateTime = At(to, days),
            Conduct = conduct,
            Description = "Bring water",
            PocName = "SGT Poc",
            PocPhone = "6598765432",
            UserPhone = phone,
        };

    private sealed class Setup(FakeGoogle google, SqlSugarScope db, string slug)
    {
        public FakeGoogle Google { get; } = google;

        public SqlSugarScope Db { get; } = db;

        public string Slug { get; } = slug;

        public Guid TenantId => Db.Queryable<Tenant>().Single(t => t.Slug == Slug).Id;

        public bool TenantExists => Db.Queryable<Tenant>().Any(t => t.Slug == Slug);

        /// <summary>The legacy stores reading the fake Google, made afresh as they are each time the importer is run.</summary>
        public ServiceProvider Legacy() => LegacyGoogle.Services(Google);

        public async Task<LegacyImportReport> ImportAsync(bool dryRun = false, bool overwrite = false, string? slug = null)
        {
            await using var provider = Legacy();
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var importer = new LegacyImporter(
                Db,
                services.GetRequiredService<IUserRepository>(),
                services.GetRequiredService<IFacilityRepository>(),
                services.GetRequiredService<INominalRollRepository>(),
                services.GetRequiredService<IBookingService>()
            );
            return await importer.ImportAsync(new LegacyImportOptions { Slug = slug ?? Slug, Name = "Test tenant", DryRun = dryRun, Overwrite = overwrite });
        }

        public async Task<LegacyVerificationReport> VerifyAsync()
        {
            await using var provider = Legacy();
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            return await new LegacyVerifier(
                Db,
                services.GetRequiredService<IUserRepository>(),
                services.GetRequiredService<IFacilityRepository>(),
                services.GetRequiredService<INominalRollRepository>(),
                services.GetRequiredService<IBookingService>()
            ).VerifyAsync(Slug);
        }

        public DefaultTenant DefaultTenant() =>
            new(Db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:TenantSlug"] = Slug }).Build());

        public List<TenantMember> Members()
        {
            var tenantId = TenantId;
            return Db.Queryable<TenantMember>().Where(m => m.TenantId == tenantId).ToList();
        }

        public List<DataBooking> Bookings()
        {
            var tenantId = TenantId;
            return Db.Queryable<DataBooking>().Where(b => b.TenantId == tenantId).ToList();
        }
    }

    private static async Task<Setup> SetUpAsync()
    {
        var google = new FakeGoogle();
        google.Sheets["Nominal Roll"] =
        [
            ["Name", "Unit", "Phone"],
            ["PTE Roster One", "Alpha", "6590000001"],
            ["PTE Roster Two", "Bravo", "6590000002"],
        ];
        var db = (await TestDatabase.SharedAsync()).CreateClient();
        return new Setup(google, db, $"imp{Guid.NewGuid():N}"[..14]);
    }

    [Test]
    public async Task Everything_on_the_sheets_and_in_the_calendar_is_imported()
    {
        var setup = await SetUpAsync();
        var bookings = new[] { Legacy("Eiger", 8, 10), Legacy("Field", 8, 10, Users.SameUnit), Legacy("Gym", 12, 14, Users.AllGroup, days: 11) };
        foreach (var booking in bookings)
        {
            setup.Google.AddBooking(booking);
        }

        var report = await setup.ImportAsync();

        await Assert.That(report.TenantCreated).IsTrue();
        // So the imported members can claim their rows with Clerk accounts in Cutover 2
        await Assert.That(setup.Db.Queryable<Tenant>().Single(t => t.Slug == setup.Slug).LegacyClaimEnabled).IsTrue();
        await Assert.That(report.Units.Added).IsEqualTo(3);
        await Assert.That(report.Members.Added).IsEqualTo(5);
        await Assert.That(report.Facilities.Added).IsEqualTo(3);
        await Assert.That(report.Roster.Added).IsEqualTo(2);
        await Assert.That(report.Bookings.Added).IsEqualTo(3);
        await Assert.That(report.Warnings).IsEmpty();

        var members = setup.Members();
        await Assert.That(members.Count).IsEqualTo(5);
        // Nobody has an account yet, so they are waiting to be claimed
        await Assert.That(members.Select(m => m.Status).Distinct()).IsEquivalentTo([MemberStatus.Unclaimed]);
        var booker = members.Single(m => m.Phone == "+" + Users.Booker);
        await Assert.That(booker.DisplayName).IsEqualTo("CPT Booker");
        await Assert.That(booker.LegacyChatId).IsEqualTo("1001");
        await Assert.That(booker.NotificationScope).IsEqualTo(NotificationScope.Unit);
        await Assert.That(members.Single(m => m.Phone == "+" + Users.Admin).Role).IsEqualTo(MemberRole.Admin);
        await Assert.That(members.Single(m => m.Phone == "+" + Users.AllGroup).NotificationScope).IsEqualTo(NotificationScope.All);

        // With the IDs they had, so the events that go with them stay the same
        var stored = setup.Bookings();
        await Assert.That(stored.Select(b => b.Id)).IsEquivalentTo(bookings.Select(b => b.Id).ToList());
        var first = stored.Single(b => b.Id == bookings[0].Id);
        await Assert.That(first.StartUtc).IsEqualTo(At(8));
        await Assert.That(first.Conduct).IsEqualTo("Section training");
        await Assert.That(first.PocPhone).IsEqualTo("6598765432");
        await Assert.That(first.Revision).IsEqualTo(1);
        await Assert.That(members.Single(m => m.Id == first.BookedByMemberId).Phone).IsEqualTo("+" + Users.Booker);
        await Assert.That(first.UnitId).IsEqualTo(booker.UnitId);
    }

    [Test]
    public async Task What_is_imported_reads_back_through_the_database_stores_as_it_was_in_google()
    {
        var setup = await SetUpAsync();
        var bookings = new[] { Legacy("Eiger", 8, 10), Legacy("Field", 8, 10, Users.SameUnit), Legacy("Gym", 12, 14, Users.AllGroup, days: 11) };
        foreach (var booking in bookings)
        {
            setup.Google.AddBooking(booking);
        }

        await setup.ImportAsync();

        await using var provider = setup.Legacy();
        using var scope = provider.CreateScope();
        var tenant = setup.DefaultTenant();

        var legacyUsers = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetListAsync()).OrderBy(u => u.Phone).ToList();
        var users = (await new DatabaseUserRepository(setup.Db, tenant).GetListAsync()).OrderBy(u => u.Phone).ToList();
        await Assert.That(users.Select(u => (u.Unit, u.Name, u.Phone, u.TelegramChatId, u.NotificationGroup, u.IsAdmin))).IsEquivalentTo(
            legacyUsers.Select(u => (u.Unit, u.Name, u.Phone, u.TelegramChatId, u.NotificationGroup, u.IsAdmin)).ToList()
        );

        var legacyFacilities = (await scope.ServiceProvider.GetRequiredService<IFacilityRepository>().GetListAsync()).OrderBy(f => f.Name).ToList();
        var facilities = (await new DatabaseFacilityRepository(setup.Db, tenant).GetListAsync()).OrderBy(f => f.Name).ToList();
        await Assert.That(facilities.Select(f => (f.Name, f.Group, string.Join(",", (f.Scope ?? []).Order())))).IsEquivalentTo(
            legacyFacilities.Select(f => (f.Name, f.Group, string.Join(",", (f.Scope ?? []).Select(s => s.Trim()).Order()))).ToList()
        );

        var legacyRoster = (await scope.ServiceProvider.GetRequiredService<INominalRollRepository>().GetListAsync()).OrderBy(r => r.Phone).ToList();
        var roster = (await new DatabaseNominalRollRepository(setup.Db, tenant).GetListAsync()).OrderBy(r => r.Phone).ToList();
        await Assert.That(roster.Select(r => (r.Name, r.Unit, r.Phone))).IsEquivalentTo(legacyRoster.Select(r => (r.Name, r.Unit, r.Phone)).ToList());

        var legacyList = (await scope.ServiceProvider.GetRequiredService<IBookingService>().ListAsync()).OrderBy(b => b.Id).ToList();
        var listed = (await new DatabaseBookingService(setup.Db, tenant, new OutboxSignal()).ListAsync()).OrderBy(b => b.Id).ToList();
        await Assert.That(listed.Select(b => (b.Id, b.FacilityName, b.StartDateTime, b.EndDateTime, b.Conduct, b.Description, b.PocName, b.PocPhone, b.UserPhone))).IsEquivalentTo(
            legacyList.Select(b => (b.Id, b.FacilityName, b.StartDateTime, b.EndDateTime, b.Conduct, b.Description, b.PocName, b.PocPhone, b.UserPhone)).ToList()
        );
    }

    [Test]
    public async Task Importing_again_changes_nothing()
    {
        var setup = await SetUpAsync();
        setup.Google.AddBooking(Legacy("Eiger", 8, 10));
        await setup.ImportAsync();
        var members = setup.Members().Count;
        var bookings = setup.Bookings().Count;

        var report = await setup.ImportAsync();

        await Assert.That(report.TenantCreated).IsFalse();
        foreach (var counts in new[] { report.Units, report.Members, report.Facilities, report.Roster, report.Bookings })
        {
            await Assert.That(counts.Added).IsEqualTo(0);
            await Assert.That(counts.Updated).IsEqualTo(0);
        }

        await Assert.That(setup.Members().Count).IsEqualTo(members);
        await Assert.That(setup.Bookings().Count).IsEqualTo(bookings);
    }

    [Test]
    public async Task A_dry_run_reports_what_would_happen_and_saves_nothing()
    {
        var setup = await SetUpAsync();
        setup.Google.AddBooking(Legacy("Eiger", 8, 10));

        var report = await setup.ImportAsync(dryRun: true);

        await Assert.That(report.DryRun).IsTrue();
        await Assert.That(report.TenantCreated).IsTrue();
        await Assert.That(report.Members.Added).IsEqualTo(5);
        await Assert.That(report.Bookings.Added).IsEqualTo(1);
        await Assert.That(setup.TenantExists).IsFalse();

        var real = await setup.ImportAsync();
        await Assert.That(real.Members.Added).IsEqualTo(5);
        await Assert.That(real.Bookings.Added).IsEqualTo(1);
    }

    [Test]
    public async Task Only_what_is_new_is_added_unless_it_is_told_to_overwrite()
    {
        var setup = await SetUpAsync();
        var booking = Legacy("Eiger", 8, 10);
        setup.Google.AddBooking(booking);
        await setup.ImportAsync();

        // Changed on the sheets and in the calendar since
        setup.Google.Sheets["Users"][1][1] = "CPT Renamed";
        setup.Google.Sheets["Users"].Add(["Alpha", "PTE New", "6590001111", "", "None", "FALSE"]);
        booking.Conduct = "Changed";
        setup.Google.AddBooking(booking);
        var another = Legacy("Gym", 8, 10, days: 12);
        setup.Google.AddBooking(another);

        var report = await setup.ImportAsync();

        await Assert.That(report.Members.Added).IsEqualTo(1);
        await Assert.That(report.Members.Updated).IsEqualTo(0);
        await Assert.That(report.Bookings.Added).IsEqualTo(1);
        await Assert.That(report.Bookings.Updated).IsEqualTo(0);
        await Assert.That(setup.Members().Single(m => m.Phone == "+" + Users.Booker).DisplayName).IsEqualTo("CPT Booker");
        await Assert.That(setup.Bookings().Single(b => b.Id == booking.Id).Conduct).IsEqualTo("Section training");

        var overwritten = await setup.ImportAsync(overwrite: true);

        await Assert.That(overwritten.Members.Updated).IsEqualTo(1);
        await Assert.That(overwritten.Bookings.Updated).IsEqualTo(1);
        await Assert.That(setup.Members().Single(m => m.Phone == "+" + Users.Booker).DisplayName).IsEqualTo("CPT Renamed");
        await Assert.That(setup.Bookings().Single(b => b.Id == booking.Id).Conduct).IsEqualTo("Changed");
        // Overwriting doesn't make a change out of what didn't change
        await Assert.That(setup.Bookings().Single(b => b.Id == booking.Id).Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Overwriting_is_refused_once_the_database_has_been_used()
    {
        var setup = await SetUpAsync();
        setup.Google.AddBooking(Legacy("Eiger", 8, 10));
        await setup.ImportAsync();
        await OutboxWriter.EnqueueAsync(setup.Db, setup.TenantId, TelegramMessageType, new { });

        await Assert.That(async () => await setup.ImportAsync(overwrite: true)).Throws<InvalidOperationException>();

        // Adding what is new is still fine
        var report = await setup.ImportAsync();
        await Assert.That(report.Bookings.Added).IsEqualTo(0);
    }

    private const string TelegramMessageType = "telegram.booking";

    [Test]
    public async Task Rows_that_cannot_be_imported_are_left_out_and_said_so()
    {
        var setup = await SetUpAsync();
        setup.Google.Sheets["Users"].Add(["Alpha", "Someone Again", Users.Booker, "9", "None", "FALSE"]);
        setup.Google.Sheets["Users"].Add(["Alpha", "No Phone", "", "9", "None", "FALSE"]);
        setup.Google.Sheets["Facilities"].Add(["Eiger", "Again", "All"]);
        setup.Google.Sheets["Nominal Roll"].Add(["PTE Twice", "Alpha", "6590000001"]);

        var report = await setup.ImportAsync();

        await Assert.That(report.Members.Added).IsEqualTo(5);
        await Assert.That(report.Facilities.Added).IsEqualTo(3);
        await Assert.That(report.Roster.Added).IsEqualTo(2);
        await Assert.That(report.Warnings.Count).IsEqualTo(4);
        await Assert.That(report.Warnings).Contains(w => w.Contains("Someone Again") && w.Contains("same phone number"));
        await Assert.That(report.Warnings).Contains(w => w.Contains("No Phone") && w.Contains("no phone number"));
        await Assert.That(report.Warnings).Contains(w => w.Contains("Eiger") && w.Contains("same name"));
        await Assert.That(report.Warnings).Contains(w => w.Contains("PTE Twice"));
        // The first of the repeats is the one that counts, as it was for the old version
        await Assert.That(setup.Members().Single(m => m.Phone == "+" + Users.Booker).DisplayName).IsEqualTo("CPT Booker");
    }

    [Test]
    public async Task Bookings_by_people_no_longer_on_the_sheet_are_kept_as_by_a_member_who_has_left()
    {
        var setup = await SetUpAsync();
        var byLeaver = Legacy("Eiger", 8, 10, phone: "6500000000");
        var byNobody = Legacy("Field", 8, 10, phone: null);
        setup.Google.AddBooking(byLeaver);
        setup.Google.AddBooking(byNobody);

        var report = await setup.ImportAsync();

        await Assert.That(report.Bookings.Added).IsEqualTo(2);
        await Assert.That(report.Members.Added).IsEqualTo(7);
        var members = setup.Members();
        var leaver = members.Single(m => m.Phone == "+6500000000");
        await Assert.That(leaver.Status).IsEqualTo(MemberStatus.Removed);
        await Assert.That(members.Single(m => m.Phone == null).DisplayName).IsEqualTo("Unknown");
        await Assert.That(setup.Bookings().Single(b => b.Id == byLeaver.Id).BookedByMemberId).IsEqualTo(leaver.Id);
        await Assert.That(report.Warnings).Contains(w => w.Contains("6500000000") && w.Contains("member who has left"));

        // The one for bookings with no number isn't made again
        setup.Google.AddBooking(Legacy("Gym", 8, 10, phone: null, days: 12));
        await setup.ImportAsync();
        await Assert.That(setup.Members().Count(m => m.Phone == null)).IsEqualTo(1);
    }

    [Test]
    public async Task Someone_who_only_appeared_on_bookings_and_then_on_the_sheet_becomes_a_member()
    {
        var setup = await SetUpAsync();
        setup.Google.AddBooking(Legacy("Eiger", 8, 10, phone: "6590009999"));
        await setup.ImportAsync();
        setup.Google.Sheets["Users"].Add(["Bravo", "PTE Came Back", "6590009999", "", "Unit", "FALSE"]);

        var report = await setup.ImportAsync();

        await Assert.That(report.Members.Updated).IsEqualTo(1);
        var member = setup.Members().Single(m => m.Phone == "+6590009999");
        await Assert.That(member.Status).IsEqualTo(MemberStatus.Unclaimed);
        await Assert.That(member.DisplayName).IsEqualTo("PTE Came Back");
    }

    [Test]
    public async Task A_facility_that_is_no_longer_on_the_sheet_but_has_bookings_is_kept_and_nobody_can_book_it()
    {
        var setup = await SetUpAsync();
        setup.Google.AddBooking(Legacy("Old Hall", 8, 10));

        var report = await setup.ImportAsync();

        await Assert.That(report.Warnings).Contains(w => w.Contains("Old Hall") && w.Contains("not on the Facilities sheet"));
        var tenantId = setup.TenantId;
        var facility = setup.Db.Queryable<DataFacility>().Single(f => f.TenantId == tenantId && f.Name == "Old Hall");
        await Assert.That(facility.AvailableToAll).IsFalse();
        await Assert.That(setup.Db.Queryable<FacilityUnitAccess>().Any(a => a.FacilityId == facility.Id)).IsFalse();
        var scoped = await new DatabaseFacilityRepository(setup.Db, setup.DefaultTenant()).GetListAsync();
        await Assert.That(scoped.Single(f => f.Name == "Old Hall").Scope).IsNull();
    }

    [Test]
    public async Task Overlapping_bookings_are_imported_as_they_are_and_pointed_out()
    {
        var setup = await SetUpAsync();
        setup.Google.AddBooking(Legacy("Eiger", 8, 11));
        setup.Google.AddBooking(Legacy("Eiger", 10, 12));
        setup.Google.AddBooking(Legacy("Field", 10, 12));

        var report = await setup.ImportAsync();

        await Assert.That(report.Bookings.Added).IsEqualTo(3);
        await Assert.That(report.Warnings).Contains(w => w.StartsWith("1 bookings overlap"));
    }

    [Test]
    public async Task Fields_that_are_longer_than_the_database_keeps_are_cut_short_and_said_so()
    {
        var setup = await SetUpAsync();
        var booking = Legacy("Eiger", 8, 10, conduct: new string('c', 300));
        booking.Description = new string('d', 2500);
        setup.Google.AddBooking(booking);

        var report = await setup.ImportAsync();

        var row = setup.Bookings().Single();
        await Assert.That(row.Conduct.Length).IsEqualTo(200);
        await Assert.That(row.Description!.Length).IsEqualTo(2000);
        await Assert.That(report.Warnings).Contains(w => w.Contains("2 booking fields"));
    }

    [Test]
    public async Task A_slug_that_is_not_a_slug_is_refused()
    {
        var setup = await SetUpAsync();

        await Assert.That(async () => await setup.ImportAsync(slug: "Not A Slug")).Throws<ArgumentException>();
        await Assert.That(async () => await setup.ImportAsync(slug: "-edge")).Throws<ArgumentException>();
    }

    [Test]
    public async Task A_failure_part_way_imports_nothing()
    {
        var setup = await SetUpAsync();
        var bad = Legacy("Eiger", 8, 10);
        // Too long for the column, which only shows when it is written, after everything before it was
        bad.FacilityName = new string('f', 150);
        setup.Google.AddBooking(bad);

        await Assert.That(async () => await setup.ImportAsync()).Throws<Exception>();

        await Assert.That(setup.TenantExists).IsFalse();
    }

    [Test]
    public async Task What_was_imported_verifies_and_what_was_changed_or_lost_does_not()
    {
        var setup = await SetUpAsync();
        var booking = Legacy("Eiger", 8, 10);
        setup.Google.AddBooking(booking);
        setup.Google.AddBooking(Legacy("Gym", 8, 10, days: 12));
        await setup.ImportAsync();

        var matching = await setup.VerifyAsync();
        await Assert.That(matching.Matches).IsTrue();
        await Assert.That(matching.Compared["Bookings"]).IsEqualTo(2);
        await Assert.That(matching.Compared["Members"]).IsEqualTo(5);

        // Something goes wrong on the way
        var tenantId = setup.TenantId;
        setup.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { DisplayName = "Someone Else" }).Where(m => m.TenantId == tenantId && m.Phone == "+" + Users.Booker).ExecuteCommand();
        setup.Db.Deleteable<DataBooking>().Where(b => b.Id == booking.Id).ExecuteCommand();
        setup.Db.Updateable<RosterEntry>().SetColumns(r => new RosterEntry { Name = "Changed" }).Where(r => r.TenantId == tenantId && r.Phone == "+6590000001").ExecuteCommand();
        setup.Db.Updateable<DataFacility>().SetColumns(f => new DataFacility { AvailableToAll = true }).Where(f => f.TenantId == tenantId && f.Name == "Gym").ExecuteCommand();

        var broken = await setup.VerifyAsync();

        await Assert.That(broken.Matches).IsFalse();
        await Assert.That(broken.Differences).Contains(d => d.StartsWith("Members:") && d.Contains("Someone Else"));
        await Assert.That(broken.Differences).Contains(d => d.StartsWith("Bookings:") && d.Contains(booking.Id.ToString()) && d.Contains("not in the database"));
        await Assert.That(broken.Differences).Contains(d => d.StartsWith("Roster:") && d.Contains("+6590000001"));
        await Assert.That(broken.Differences).Contains(d => d.StartsWith("Facilities:") && d.Contains("'Gym'"));
    }

    [Test]
    public async Task Verifying_finds_what_is_in_the_database_and_not_in_google()
    {
        var setup = await SetUpAsync();
        setup.Google.AddBooking(Legacy("Eiger", 8, 10));
        await setup.ImportAsync();
        var extra = Legacy("Gym", 8, 10, days: 12);
        var tenantId = setup.TenantId;
        var memberId = setup.Members().First().Id;
        var facilityId = setup.Db.Queryable<DataFacility>().Single(f => f.TenantId == tenantId && f.Name == "Gym").Id;
        setup.Db.Insertable(
                new DataBooking
                {
                    Id = extra.Id,
                    TenantId = tenantId,
                    FacilityId = facilityId,
                    StartUtc = At(8, 12),
                    EndUtc = At(10, 12),
                    Conduct = "Only here",
                    BookedByMemberId = memberId,
                }
            )
            .ExecuteCommand();

        var report = await setup.VerifyAsync();

        await Assert.That(report.Differences).Contains(d => d.Contains(extra.Id.ToString()) && d.Contains("not in the calendar"));
    }

    [Test]
    public async Task Verifying_a_tenant_that_does_not_exist_says_so()
    {
        var setup = await SetUpAsync();

        var report = await setup.VerifyAsync();

        await Assert.That(report.Matches).IsFalse();
        await Assert.That(report.Differences).Contains(d => d.Contains("does not exist"));
    }
}
