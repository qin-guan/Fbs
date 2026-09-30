using System.Text.Json;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Notifications;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Booking = Fbs.WebApi.Entities.Booking;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Who is told about a booking change and what they are told, against the database and a fake Telegram,
/// without the API in front.
/// </summary>
public class TelegramBookingNotifierTests
{
    private const string BookerPhone = "6591000001";
    private const string ColleaguePhone = "6591000002";

    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private static DateTimeOffset At(double hours, int days = 10)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(days).AddHours(hours);
    }

    private static Booking NewBooking(string facility, double from, double to, int days = 10) =>
        new()
        {
            FacilityName = facility,
            StartDateTime = At(from, days),
            EndDateTime = At(to, days),
            Conduct = "Section training",
            Description = "Bring water",
            PocName = "SGT Poc",
            PocPhone = "6598765432",
            UserPhone = BookerPhone,
        };

    private sealed class Setup(TestTenant tenant, FakeTelegram telegram)
    {
        public TestTenant Tenant { get; } = tenant;

        public FakeTelegram Telegram { get; } = telegram;

        public Guid Booker { get; set; }

        public Guid Colleague { get; set; }

        public TelegramBookingNotifier Notifier(Microsoft.Extensions.Configuration.IConfiguration? configuration = null) =>
            new(
                Tenant.Db,
                new TelegramBotClient("123456:test-token", new HttpClient(Telegram.CreateHandler())),
                new TelegramThrottle(configuration ?? new ConfigurationBuilder().Build()),
                NullLogger<TelegramBookingNotifier>.Instance
            );

        public DatabaseBookingService Service { get; set; } = null!;

        public Task<CreateResult> CreateAsync(params Booking[] bookings) => Service.CreateAsync(bookings);

        public List<OutboxMessage> Messages()
        {
            var tenantId = Tenant.TenantId;
            return Tenant.Db.Queryable<OutboxMessage>().Where(m => m.TenantId == tenantId).OrderBy(m => m.CreatedAt).ToList();
        }

        public async Task HandleAsync(OutboxMessage message) => await Notifier().HandleAsync(message, CancellationToken.None);
    }

    private static async Task<Setup> SetUpAsync()
    {
        var tenant = await TestTenant.CreateAsync();
        var alpha = tenant.AddUnit("Alpha");
        var bravo = tenant.AddUnit("Bravo");
        var charlie = tenant.AddUnit("Charlie");
        var setup = new Setup(tenant, new FakeTelegram())
        {
            Service = tenant.BookingServiceFor(await tenant.NewClientAsync()),
            Booker = tenant.AddMember("CPT Booker", "+" + BookerPhone, alpha, telegramChatId: "1001", scope: NotificationScope.None),
            Colleague = tenant.AddMember("LTA Colleague", "+" + ColleaguePhone, alpha, telegramChatId: "1002", scope: NotificationScope.Unit),
        };
        tenant.AddMember("3SG Everyone", "+6591000003", bravo, telegramChatId: "1003", scope: NotificationScope.All);
        tenant.AddMember("PTE Other Unit", "+6591000004", bravo, telegramChatId: "1004", scope: NotificationScope.Unit);
        tenant.AddMember("MAJ Not Interested", "+6591000005", charlie, telegramChatId: "1005", scope: NotificationScope.None);
        tenant.AddMember("PTE No Telegram", "+6591000006", alpha, scope: NotificationScope.Unit);
        tenant.AddMember("PTE Left", "+6591000007", alpha, status: MemberStatus.Removed, telegramChatId: "1006", scope: NotificationScope.All);
        tenant.AddFacility("Eiger", all: true);
        tenant.AddFacility("Field", all: true);
        return setup;
    }

    private static List<long> Chats(FakeTelegram telegram) => telegram.Messages.Select(m => m.ChatId).Order().ToList();

    [Test]
    public async Task The_booker_their_unit_and_everyone_who_asked_to_hear_are_told_and_nobody_else()
    {
        var setup = await SetUpAsync();
        await setup.CreateAsync(NewBooking("Field", 8, 10));

        await setup.HandleAsync(setup.Messages().Single());

        // The booker (who asked for nothing), the unit's, the "All" one. Not another unit, not "None", not
        // someone with no Telegram, not someone who has left
        await Assert.That(Chats(setup.Telegram)).IsEquivalentTo([1001L, 1002L, 1003L]);
        var text = setup.Telegram.Messages.First().Text;
        await Assert.That(text).Contains("CREATED");
        await Assert.That(text).Contains("Field");
        await Assert.That(text).Contains("Booked by: Alpha / CPT Booker, 6591000001");
        await Assert.That(text).Contains("POC: SGT Poc, 6598765432");
    }

    [Test]
    public async Task Times_are_in_the_tenants_time_zone_rather_than_the_servers()
    {
        var setup = await SetUpAsync();
        await setup.CreateAsync(NewBooking("Field", 8, 10));

        await setup.HandleAsync(setup.Messages().Single());

        await Assert.That(setup.Telegram.Messages.First().Text).Contains(" 08:00 – ").And.Contains(" 10:00\n");
    }

    [Test]
    public async Task Someone_with_no_telegram_who_made_the_booking_does_not_stop_the_others_hearing()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        booking.UserPhone = "6591000006";
        await setup.CreateAsync(booking);

        await setup.HandleAsync(setup.Messages().Single());

        // Their unit and the "All" one; there was nobody to tell in the booker's own chat
        await Assert.That(Chats(setup.Telegram)).IsEquivalentTo([1002L, 1003L]);
    }

    [Test]
    public async Task Someone_who_has_blocked_the_bot_is_skipped_and_the_others_are_still_told()
    {
        var setup = await SetUpAsync();
        setup.Telegram.BlockedChatIds.Add(1002);
        await setup.CreateAsync(NewBooking("Field", 8, 10));

        await setup.HandleAsync(setup.Messages().Single());

        await Assert.That(Chats(setup.Telegram)).IsEquivalentTo([1001L, 1003L]);
    }

    [Test]
    public async Task A_failure_is_tried_again_without_telling_twice_the_people_who_were_told()
    {
        var setup = await SetUpAsync();
        setup.Telegram.FailingChatIds.Add(1003);
        await setup.CreateAsync(NewBooking("Field", 8, 10));
        var message = setup.Messages().Single();

        await Assert.That(async () => await setup.HandleAsync(message)).Throws<Exception>();

        await Assert.That(Chats(setup.Telegram)).IsEquivalentTo([1001L, 1002L]);
        var saved = JsonSerializer.Deserialize<TelegramBookingPayload>(setup.Messages().Single().Payload)!;
        await Assert.That(saved.Delivered.Count).IsEqualTo(2);

        setup.Telegram.FailingChatIds.Clear();
        await setup.HandleAsync(setup.Messages().Single());

        await Assert.That(Chats(setup.Telegram)).IsEquivalentTo([1001L, 1002L, 1003L]);
    }

    [Test]
    public async Task An_update_says_where_it_was_and_who_changed_it()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.CreateAsync(booking);
        var move = NewBooking("Field", 12, 14);
        move.Id = booking.Id;

        await setup.Service.UpdateAsync(move, ColleaguePhone, checkForClash: true);
        await setup.HandleAsync(setup.Messages()[1]);

        var text = setup.Telegram.Messages.First().Text;
        await Assert.That(text).Contains("UPDATED");
        await Assert.That(text).Contains("Was: ").And.Contains(" 08:00 – ").And.Contains(" 10:00");
        await Assert.That(text).Contains("Updated by: Alpha / LTA Colleague, 6591000002");
        await Assert.That(Chats(setup.Telegram)).IsEquivalentTo([1001L, 1002L, 1003L]);
    }

    [Test]
    public async Task A_change_that_does_not_move_it_has_no_was()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.CreateAsync(booking);
        var rename = NewBooking("Field", 8, 10);
        rename.Id = booking.Id;
        rename.Conduct = "Renamed";

        await setup.Service.UpdateAsync(rename, BookerPhone, checkForClash: false);
        await setup.HandleAsync(setup.Messages()[1]);

        await Assert.That(setup.Telegram.Messages.First().Text).DoesNotContain("Was:");
    }

    [Test]
    public async Task A_cancellation_says_who_cancelled_it()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.CreateAsync(booking);

        await setup.Service.DeleteAsync(booking.Id, ColleaguePhone);
        await setup.HandleAsync(setup.Messages()[1]);

        var text = setup.Telegram.Messages.First().Text;
        await Assert.That(text).Contains("CANCELLED");
        await Assert.That(text).Contains("Cancelled by: Alpha / LTA Colleague, 6591000002");
    }

    [Test]
    public async Task Bookings_made_together_are_one_message_for_each_person()
    {
        var setup = await SetUpAsync();
        await setup.CreateAsync(NewBooking("Field", 8, 10, days: 10), NewBooking("Eiger", 8, 10, days: 11), NewBooking("Field", 8, 10, days: 12));

        await setup.HandleAsync(setup.Messages().Single());

        await Assert.That(Chats(setup.Telegram)).IsEquivalentTo([1001L, 1002L, 1003L]);
        await Assert.That(setup.Telegram.Messages.First().Text).Contains("3 bookings");
    }

    [Test]
    public async Task A_message_about_bookings_that_do_not_exist_cannot_be_helped_by_trying_again()
    {
        var setup = await SetUpAsync();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = setup.Tenant.TenantId,
            Type = TelegramBookingNotifier.MessageType,
            Payload = JsonSerializer.Serialize(new TelegramBookingPayload { BookingIds = [Guid.NewGuid()] }),
        };

        await Assert.That(async () => await setup.HandleAsync(message)).Throws<OutboxPermanentFailureException>();
    }

    [Test]
    public async Task A_message_that_makes_no_sense_cannot_be_helped_by_trying_again()
    {
        var setup = await SetUpAsync();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = setup.Tenant.TenantId,
            Type = TelegramBookingNotifier.MessageType,
            Payload = "{not json",
        };

        await Assert.That(async () => await setup.HandleAsync(message)).Throws<OutboxPermanentFailureException>();
    }

    [Test]
    public async Task Sending_is_kept_to_a_number_of_messages_a_second()
    {
        var throttle = new TelegramThrottle(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:MessagesPerSecond"] = "10" }).Build()
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 30; i++)
        {
            await throttle.WaitAsync(CancellationToken.None);
        }

        // 10 at once, then 10 a second: two more seconds for the other 20
        await Assert.That(stopwatch.Elapsed).IsBetween(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(6));
    }

    private static async Task<OutboxDispatcher> DispatcherFor(Setup setup) =>
        new(
            NullLogger<OutboxDispatcher>.Instance,
            await setup.Tenant.NewClientAsync(),
            new ServiceCollection().AddSingleton<IOutboxHandler>(setup.Notifier()).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new OutboxSignal(),
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions { TenantId = setup.Tenant.TenantId })
        );

    private static void SetStatus(Setup setup, TenantStatus status) =>
        setup.Tenant.Db.Updateable<Tenant>().SetColumns(t => new Tenant { Status = status }).Where(t => t.Id == setup.Tenant.TenantId).ExecuteCommand();

    [Test]
    [Arguments(TenantStatus.Suspended)]
    [Arguments(TenantStatus.PendingDeletion)]
    public async Task Nobody_is_told_about_an_organisation_that_cannot_be_used_and_what_was_not_sent_is_not_sent_later(TenantStatus status)
    {
        var setup = await SetUpAsync();
        await setup.CreateAsync(NewBooking("Field", 8, 10));
        var message = setup.Messages().Single();
        SetStatus(setup, status);
        var dispatcher = await DispatcherFor(setup);

        await dispatcher.ProcessDueAsync();

        await Assert.That(setup.Telegram.Messages).IsEmpty();
        await Assert.That(setup.Messages().Single(m => m.Id == message.Id).Status).IsEqualTo(OutboxStatus.Skipped);

        // Made active again it is not sent, as it is about what happened then, and what happens next is
        SetStatus(setup, TenantStatus.Active);
        await dispatcher.ProcessDueAsync();
        await Assert.That(setup.Telegram.Messages).IsEmpty();
        await setup.CreateAsync(NewBooking("Eiger", 8, 10));
        await dispatcher.ProcessDueAsync();
        await Assert.That(Chats(setup.Telegram)).IsEquivalentTo([1001L, 1002L, 1003L]);
    }

    [Test]
    public async Task A_message_taken_just_before_the_organisation_was_suspended_tells_nobody_either()
    {
        var setup = await SetUpAsync();
        await setup.CreateAsync(NewBooking("Field", 8, 10));
        SetStatus(setup, TenantStatus.Suspended);

        // Handled directly, as if the dispatcher had taken it a moment before
        await setup.HandleAsync(setup.Messages().Single());

        await Assert.That(setup.Telegram.Messages).IsEmpty();
    }

    [Test]
    public async Task Each_person_told_is_counted_by_whether_it_got_to_them_and_how_long_telegram_took_is_timed()
    {
        var setup = await SetUpAsync();
        setup.Telegram.BlockedChatIds.Add(1002);
        setup.Telegram.FailingChatIds.Add(1003);
        await setup.CreateAsync(NewBooking("Field", 8, 10));
        using var metrics = new MetricsRecorder();

        await Assert.That(async () => await setup.HandleAsync(setup.Messages().Single())).Throws<Exception>();

        await Assert.That(metrics.Sum("fbs.telegram.messages", ("result", "sent"))).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.telegram.messages", ("result", "blocked"))).IsEqualTo(1);
        await Assert.That(metrics.Sum("fbs.telegram.messages", ("result", "failed"))).IsEqualTo(1);
        // Each time Telegram was asked, however it went
        await Assert.That(metrics.Recorded("fbs.telegram.send.duration")).IsEqualTo(3);

        // Tried again, those who were told are not told, or counted, again
        setup.Telegram.FailingChatIds.Clear();
        await setup.HandleAsync(setup.Messages().Single());
        await Assert.That(metrics.Sum("fbs.telegram.messages", ("result", "sent"))).IsEqualTo(2);
        await Assert.That(metrics.Sum("fbs.telegram.messages")).IsEqualTo(4);
    }

    [Test]
    public async Task Nobody_told_is_nothing_counted_for_an_organisation_that_cannot_be_used()
    {
        var setup = await SetUpAsync();
        await setup.CreateAsync(NewBooking("Field", 8, 10));
        SetStatus(setup, TenantStatus.Suspended);
        using var metrics = new MetricsRecorder();

        await setup.HandleAsync(setup.Messages().Single());

        await Assert.That(metrics.Sum("fbs.telegram.messages")).IsEqualTo(0);
        await Assert.That(metrics.Recorded("fbs.telegram.send.duration")).IsEqualTo(0);
    }
}
