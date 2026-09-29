using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Legacy;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Repository.Database;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Booking = Fbs.WebApi.Entities.Booking;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Going back: writing what was booked, changed and cancelled in the database back to Google Calendar, with
/// the real legacy stores reading a fake Google, and TiDB.
/// </summary>
public class LegacyExporterTests
{
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private static DateTimeOffset At(double hours, int days = 10)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(days).AddHours(hours);
    }

    private static Booking Legacy(string facility, double from, double to, string phone = Users.Booker, int days = 10) =>
        new()
        {
            Id = Guid.NewGuid(),
            FacilityName = facility,
            StartDateTime = At(from, days),
            EndDateTime = At(to, days),
            Conduct = "Section training",
            Description = "Bring water",
            PocName = "SGT Poc",
            PocPhone = "6598765432",
            UserPhone = phone,
        };

    /// <summary>What someone does in the API, in the database, after the switch.</summary>
    private static Booking Made(string facility, double from, double to, string phone = Users.Booker, int days = 20) => Legacy(facility, from, to, phone, days);

    private sealed class Setup(FakeGoogle google, SqlSugarScope db, string slug)
    {
        public FakeGoogle Google { get; } = google;

        public SqlSugarScope Db { get; } = db;

        public string Slug { get; } = slug;

        public DefaultTenant DefaultTenant() =>
            new(Db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:TenantSlug"] = Slug }).Build());

        public DatabaseBookingService Service() => new(Db, DefaultTenant(), new OutboxSignal());

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

        public async Task<LegacyExportReport> ExportAsync(bool dryRun = false, string? slug = null)
        {
            await using var provider = LegacyGoogle.Services(Google);
            using var scope = provider.CreateScope();
            return await new LegacyExporter(Db, scope.ServiceProvider.GetRequiredService<BookingRepository>()).ExportAsync(slug ?? Slug, dryRun);
        }

        /// <summary>What the old version would show now.</summary>
        public async Task<List<Booking>> LegacyBookingsAsync()
        {
            await using var provider = LegacyGoogle.Services(Google);
            using var scope = provider.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IBookingService>().ListAsync()).OrderBy(b => b.StartDateTime).ToList();
        }

        public async Task<LegacyVerificationReport> VerifyAsync()
        {
            await using var provider = LegacyGoogle.Services(Google);
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

        public int Writes() => Google.Requests.Count(r => r.Method != HttpMethod.Get);
    }

    private static async Task<(Setup Setup, Booking[] Imported)> SetUpAsync()
    {
        var google = new FakeGoogle();
        google.Sheets["Nominal Roll"] = [["Name", "Unit", "Phone"]];
        var imported = new[] { Legacy("Eiger", 8, 10, days: 10), Legacy("Field", 8, 10, Users.SameUnit, days: 11), Legacy("Gym", 8, 10, Users.AllGroup, days: 12) };
        foreach (var booking in imported)
        {
            google.AddBooking(booking);
        }

        var setup = new Setup(google, (await TestDatabase.SharedAsync()).CreateClient(), $"exp{Guid.NewGuid():N}"[..14]);
        await setup.ImportAsync();
        return (setup, imported);
    }

    [Test]
    public async Task What_was_booked_changed_and_cancelled_since_the_switch_is_written_back()
    {
        var (setup, imported) = await SetUpAsync();
        var service = setup.Service();
        var added = Made("Eiger", 8, 10);
        await service.CreateAsync([added]);
        var change = Legacy("Eiger", 12, 14, days: 10);
        change.Id = imported[0].Id;
        change.Conduct = "Moved and renamed";
        await service.UpdateAsync(change, Users.SameUnit, checkForClash: true);
        await service.DeleteAsync(imported[1].Id, Users.Booker);

        var report = await setup.ExportAsync();

        await Assert.That(report.Bookings.Added).IsEqualTo(1);
        await Assert.That(report.Bookings.Updated).IsEqualTo(1);
        await Assert.That(report.Bookings.Unchanged).IsEqualTo(1);
        await Assert.That(report.Removed).IsEqualTo(1);
        await Assert.That(report.Failures).IsEmpty();

        var legacy = await setup.LegacyBookingsAsync();
        await Assert.That(legacy.Select(b => b.Id)).IsEquivalentTo([imported[0].Id, imported[2].Id, added.Id]);
        var moved = legacy.Single(b => b.Id == imported[0].Id);
        await Assert.That(moved.Conduct).IsEqualTo("Moved and renamed");
        await Assert.That(moved.StartDateTime).IsEqualTo(At(12));
        // Made by whoever made it, not by who last changed it
        await Assert.That(moved.UserPhone).IsEqualTo(Users.Booker);
        await Assert.That(legacy.Single(b => b.Id == added.Id).UserPhone).IsEqualTo(Users.Booker);

        // In both calendars, with the IDs they have in the database
        foreach (var calendar in new[] { FakeGoogle.MainCalendar, FakeGoogle.CarbonCopyCalendar })
        {
            await Assert
                .That(setup.Google.Events(calendar).Select(e => e["id"]!.GetValue<string>()))
                .IsEquivalentTo([imported[0].Id.ToString("N"), imported[2].Id.ToString("N"), added.Id.ToString("N")]);
        }
    }

    [Test]
    public async Task Writing_back_a_second_time_writes_nothing()
    {
        var (setup, _) = await SetUpAsync();
        await setup.Service().CreateAsync([Made("Eiger", 8, 10)]);
        await setup.ExportAsync();
        var writes = setup.Writes();

        var again = await setup.ExportAsync();

        await Assert.That(again.Bookings.Added).IsEqualTo(0);
        await Assert.That(again.Bookings.Updated).IsEqualTo(0);
        await Assert.That(again.Removed).IsEqualTo(0);
        await Assert.That(again.Bookings.Unchanged).IsEqualTo(4);
        await Assert.That(setup.Writes()).IsEqualTo(writes);
    }

    [Test]
    public async Task A_dry_run_says_what_would_be_written_and_writes_nothing()
    {
        var (setup, imported) = await SetUpAsync();
        await setup.Service().CreateAsync([Made("Eiger", 8, 10)]);
        await setup.Service().DeleteAsync(imported[2].Id, Users.Booker);
        var writes = setup.Writes();

        var report = await setup.ExportAsync(dryRun: true);

        await Assert.That(report.DryRun).IsTrue();
        await Assert.That(report.Bookings.Added).IsEqualTo(1);
        await Assert.That(report.Removed).IsEqualTo(1);
        await Assert.That(setup.Writes()).IsEqualTo(writes);
        await Assert.That((await setup.LegacyBookingsAsync()).Count).IsEqualTo(3);
    }

    [Test]
    public async Task What_is_written_back_imports_as_the_same_bookings_and_verifies()
    {
        var (setup, imported) = await SetUpAsync();
        var service = setup.Service();
        await service.CreateAsync([Made("Eiger", 8, 10), Made("Gym", 8, 10, Users.AllGroup, days: 21)]);
        await service.DeleteAsync(imported[0].Id, Users.Booker);
        await setup.ExportAsync();

        var verified = await setup.VerifyAsync();

        await Assert.That(verified.Differences).IsEmpty();
        await Assert.That(verified.Matches).IsTrue();
    }

    [Test]
    public async Task A_booking_that_cannot_be_written_is_reported_and_the_rest_are_still_written()
    {
        var (setup, _) = await SetUpAsync();
        var service = setup.Service();
        var fine = Made("Eiger", 8, 10, Users.SameUnit);
        var byStranger = Made("Field", 8, 10, Users.Booker);
        var tooLong = Made("Gym", 8, 10, Users.SameUnit);
        tooLong.Description = new string('d', 1900);
        await service.CreateAsync([fine, byStranger, tooLong]);
        // Since taken off the Users sheet, which the old version needs to know who booked
        setup.Google.Sheets["Users"].RemoveAll(row => row[2] == Users.Booker);

        var report = await setup.ExportAsync();

        await Assert.That(report.Bookings.Added).IsEqualTo(1);
        await Assert.That(report.Failures.Count).IsEqualTo(2);
        await Assert.That(report.Failures).Contains(f => f.Contains(byStranger.Id.ToString()));
        await Assert.That(report.Failures).Contains(f => f.Contains(tooLong.Id.ToString()));
        await Assert.That((await setup.LegacyBookingsAsync()).Select(b => b.Id)).Contains(fine.Id);
        await Assert.That((await setup.LegacyBookingsAsync()).Count).IsEqualTo(4);
    }

    [Test]
    public async Task A_booking_with_more_written_on_it_than_an_event_can_hold_is_reported()
    {
        var (setup, _) = await SetUpAsync();
        var tooLong = Made("Eiger", 8, 10);
        tooLong.Description = new string('d', 1900);
        await setup.Service().CreateAsync([tooLong]);

        var report = await setup.ExportAsync();

        await Assert.That(report.Failures).HasSingleItem();
        await Assert.That(report.Failures[0]).Contains(tooLong.Id.ToString());
        await Assert.That(report.Failures[0]).Contains("too long");
    }

    [Test]
    public async Task A_tenant_that_does_not_exist_is_refused()
    {
        var (setup, _) = await SetUpAsync();

        await Assert.That(async () => await setup.ExportAsync(slug: "nobody-here")).Throws<InvalidOperationException>();
    }
}
