using Fbs.WebApi.Bookings;
using Fbs.WebApi.CalendarSync;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Outbox;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Booking = Fbs.WebApi.Entities.Booking;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Copying bookings to a tenant's Google Calendar, against the database and a fake Google.
/// </summary>
public class CalendarBookingSyncTests
{
    private const string BookerPhone = "6591000001";
    private const string ColleaguePhone = "6591000002";
    private const string CalendarId = "tenant-calendar";

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

    private sealed class Setup(TestTenant tenant, FakeGoogle google, CalendarService calendar, OutboxSignal signal)
    {
        public TestTenant Tenant { get; } = tenant;

        public FakeGoogle Google { get; } = google;

        public CalendarService Calendar { get; } = calendar;

        public OutboxSignal Signal { get; } = signal;

        public DatabaseBookingService Service { get; set; } = null!;

        public Guid Booker { get; set; }

        public void Connect(string calendarId = CalendarId) =>
            Tenant.Db.Insertable(new CalendarConnection { Id = Guid.NewGuid(), TenantId = Tenant.TenantId, CalendarId = calendarId }).ExecuteCommand();

        public IReadOnlyList<System.Text.Json.Nodes.JsonObject> Events(string calendarId = CalendarId) => Google.Events(calendarId);

        public async Task<OutboxDispatcher> DispatcherAsync()
        {
            var handler = new CalendarBookingSync(await Tenant.NewClientAsync(), Calendar, NullLogger<CalendarBookingSync>.Instance);
            var services = new ServiceCollection();
            services.AddSingleton<IOutboxHandler>(handler);
            return new OutboxDispatcher(
                NullLogger<OutboxDispatcher>.Instance,
                await Tenant.NewClientAsync(),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                Signal,
                Microsoft.Extensions.Options.Options.Create(new OutboxOptions { TenantId = Tenant.TenantId, MaxAttempts = 3 })
            );
        }

        public async Task DispatchAsync() => await (await DispatcherAsync()).ProcessDueAsync();

        public List<OutboxMessage> Messages(string type = CalendarOutbox.MessageType)
        {
            var tenantId = Tenant.TenantId;
            return Tenant.Db.Queryable<OutboxMessage>().Where(m => m.TenantId == tenantId && m.Type == type).OrderBy(m => m.CreatedAt).ToList();
        }

        public void MakeAllDue()
        {
            var tenantId = Tenant.TenantId;
            Tenant.Db.Updateable<OutboxMessage>().SetColumns(m => new OutboxMessage { NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1) }).Where(m => m.TenantId == tenantId && m.Status == OutboxStatus.Pending).ExecuteCommand();
        }

        public BookingCalendarEvent? State(Guid bookingId) => Tenant.Db.Queryable<BookingCalendarEvent>().First(s => s.BookingId == bookingId);

        public CalendarConnection Connection()
        {
            var tenantId = Tenant.TenantId;
            return Tenant.Db.Queryable<CalendarConnection>().Single(c => c.TenantId == tenantId);
        }

        public int GoogleCalls() => Google.Requests.Count(r => r.Path.Contains($"calendars/{CalendarId}/events"));
    }

    private static async Task<Setup> SetUpAsync(bool connected = true)
    {
        var tenant = await TestTenant.CreateAsync();
        var google = new FakeGoogle();
        var calendar = new CalendarService(new BaseClientService.Initializer { ApplicationName = "Tests", HttpClientFactory = google.CreateHttpClientFactory() });
        var signal = new OutboxSignal();
        var alpha = tenant.AddUnit("Alpha");
        tenant.AddFacility("Field", all: true);
        tenant.AddFacility("Eiger", all: true);
        var setup = new Setup(tenant, google, calendar, signal) { Booker = tenant.AddMember("CPT Booker", "+" + BookerPhone, alpha) };
        tenant.AddMember("LTA Colleague", "+" + ColleaguePhone, alpha);
        setup.Service = tenant.BookingServiceFor(await tenant.NewClientAsync(), signal);
        if (connected)
        {
            setup.Connect();
        }

        return setup;
    }

    [Test]
    public async Task A_booking_is_added_to_the_calendar_with_its_details()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);

        await setup.DispatchAsync();

        var @event = await Assert.That(setup.Events()).HasSingleItem();
        await Assert.That(@event["id"]!.GetValue<string>()).IsEqualTo(booking.Id.ToString("N"));
        await Assert.That(@event["summary"]!.GetValue<string>()).IsEqualTo("Alpha Section training");
        await Assert.That(@event["location"]!.GetValue<string>()).IsEqualTo("Field");
        await Assert.That(@event["description"]!.GetValue<string>()).Contains("Point of contact: SGT Poc / 6598765432");
        await Assert.That(@event["description"]!.GetValue<string>()).Contains("Booked by: Alpha / CPT Booker");
        await Assert.That(@event["description"]!.GetValue<string>()).Contains("Number: 6591000001");
        await Assert.That(@event["description"]!.GetValue<string>()).Contains("Bring water");
        await Assert.That(DateTimeOffset.Parse(@event["start"]!["dateTime"]!.GetValue<string>())).IsEqualTo(At(8));
        await Assert.That(@event["start"]!["timeZone"]!.GetValue<string>()).IsEqualTo("Asia/Singapore");
        var state = setup.State(booking.Id)!;
        await Assert.That(state.SyncedRevision).IsEqualTo(1);
        await Assert.That(state.CalendarId).IsEqualTo(CalendarId);
        await Assert.That(state.EventId).IsEqualTo(booking.Id.ToString("N"));
    }

    [Test]
    public async Task A_change_updates_the_event_and_a_cancellation_removes_it()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        await setup.DispatchAsync();
        var change = NewBooking("Field", 12, 14);
        change.Id = booking.Id;
        change.Conduct = "Renamed";

        await setup.Service.UpdateAsync(change, ColleaguePhone, checkForClash: true);
        await setup.DispatchAsync();

        var @event = await Assert.That(setup.Events()).HasSingleItem();
        await Assert.That(@event["summary"]!.GetValue<string>()).IsEqualTo("Alpha Renamed");
        await Assert.That(DateTimeOffset.Parse(@event["start"]!["dateTime"]!.GetValue<string>())).IsEqualTo(At(12));
        await Assert.That(setup.State(booking.Id)!.SyncedRevision).IsEqualTo(2);

        await setup.Service.DeleteAsync(booking.Id, ColleaguePhone);
        await setup.DispatchAsync();

        await Assert.That(setup.Events()).IsEmpty();
        await Assert.That(setup.State(booking.Id)).IsNull();
    }

    [Test]
    public async Task Every_booking_in_a_batch_is_added()
    {
        var setup = await SetUpAsync();
        var bookings = new[] { NewBooking("Field", 8, 10, 10), NewBooking("Eiger", 8, 10, 11), NewBooking("Field", 8, 10, 12) };
        await setup.Service.CreateAsync(bookings);

        await setup.DispatchAsync();

        await Assert.That(setup.Events().Select(e => e["id"]!.GetValue<string>())).IsEquivalentTo(bookings.Select(b => b.Id.ToString("N")).ToList());
    }

    [Test]
    public async Task An_event_that_is_already_in_the_calendar_is_updated_not_added_a_second_time()
    {
        // As with 3SIB's calendar, which already has events with the IDs bookings will keep
        var setup = await SetUpAsync(connected: false);
        setup.Connect(FakeGoogle.CarbonCopyCalendar);
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        setup.Google.AddBooking(new Booking { Id = booking.Id, Conduct = "Old title", FacilityName = "Field", StartDateTime = At(8), EndDateTime = At(10), UserPhone = BookerPhone });
        var added = setup.Google.Requests.Count(r => r.Method == HttpMethod.Post);

        await setup.DispatchAsync();

        var @event = await Assert.That(setup.Events(FakeGoogle.CarbonCopyCalendar)).HasSingleItem();
        await Assert.That(@event["summary"]!.GetValue<string>()).IsEqualTo("Alpha Section training");
        await Assert.That(setup.Google.Requests.Count(r => r.Method == HttpMethod.Post)).IsEqualTo(added);
    }

    [Test]
    public async Task An_event_whose_id_was_used_by_one_that_was_deleted_is_brought_back()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        await setup.DispatchAsync();
        // Deleted by hand in the calendar
        setup.Google.RemoveEvent(CalendarId, booking.Id);
        await Assert.That(setup.Events()).IsEmpty();
        var change = NewBooking("Field", 8, 10);
        change.Id = booking.Id;
        change.Conduct = "Renamed";

        await setup.Service.UpdateAsync(change, BookerPhone, checkForClash: false);
        await setup.DispatchAsync();

        var @event = await Assert.That(setup.Events()).HasSingleItem();
        await Assert.That(@event["summary"]!.GetValue<string>()).IsEqualTo("Alpha Renamed");
    }

    [Test]
    public async Task A_booking_that_is_up_to_date_is_not_sent_again()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        await setup.DispatchAsync();
        var calls = setup.GoogleCalls();
        await OutboxWriter.EnqueueAsync(setup.Tenant.Db, setup.Tenant.TenantId, CalendarOutbox.MessageType, new CalendarBookingPayload { BookingId = booking.Id });

        await setup.DispatchAsync();

        await Assert.That(setup.GoogleCalls()).IsEqualTo(calls);
        await Assert.That(setup.Messages().Select(m => m.Status).Distinct()).IsEquivalentTo([OutboxStatus.Done]);
    }

    [Test]
    public async Task Cancelling_what_is_not_in_the_calendar_is_fine()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        await setup.Service.DeleteAsync(booking.Id, BookerPhone);

        await setup.DispatchAsync();

        await Assert.That(setup.Events()).IsEmpty();
        await Assert.That(setup.Messages().Select(m => m.Status).Distinct()).IsEquivalentTo([OutboxStatus.Done]);
    }

    [Test]
    public async Task A_calendar_that_is_refused_is_marked_failed_and_nothing_more_is_sent_to_it()
    {
        var setup = await SetUpAsync();
        setup.Google.ForbiddenCalendars.Add(CalendarId);
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);

        await setup.DispatchAsync();

        var message = setup.Messages().Single();
        await Assert.That(message.Status).IsEqualTo(OutboxStatus.Dead);
        var connection = setup.Connection();
        await Assert.That(connection.Status).IsEqualTo(CalendarConnectionStatus.Failed);
        await Assert.That(connection.LastError).Contains("403");

        // And it isn't tried for the next booking, until it is put right
        await setup.Service.CreateAsync([NewBooking("Field", 12, 14)]);
        await Assert.That(setup.Messages().Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_problem_at_googles_end_is_tried_again_later()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        setup.Google.FailingCalendars.Add(CalendarId);
        await setup.Service.CreateAsync([booking]);

        await setup.DispatchAsync();

        var message = setup.Messages().Single();
        await Assert.That(message.Status).IsEqualTo(OutboxStatus.Pending);
        await Assert.That(message.LastError).IsNotNull();
        await Assert.That(setup.Connection().Status).IsEqualTo(CalendarConnectionStatus.Active);

        setup.Google.FailingCalendars.Clear();
        setup.MakeAllDue();
        await setup.DispatchAsync();

        await Assert.That(setup.Events()).HasSingleItem();
        await Assert.That(setup.Messages().Single().Status).IsEqualTo(OutboxStatus.Done);
    }

    [Test]
    public async Task Being_told_to_slow_down_is_tried_again_and_does_not_fail_the_calendar()
    {
        var setup = await SetUpAsync();
        setup.Google.RateLimitedCalendars.Add(CalendarId);
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);

        await setup.DispatchAsync();

        await Assert.That(setup.Messages().Single().Status).IsEqualTo(OutboxStatus.Pending);
        await Assert.That(setup.Connection().Status).IsEqualTo(CalendarConnectionStatus.Active);
    }

    [Test]
    public async Task A_slow_send_of_an_old_version_does_not_leave_the_calendar_out_of_date()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        // Another instance sends the version that came next while this one's is still on its way to Google, and lands first
        setup.Google.Latency = TimeSpan.FromMilliseconds(800);

        Task slow;
        using (ExecutionContext.SuppressFlow())
        {
            slow = Task.Run(() => setup.DispatchAsync());
        }

        await Task.Delay(300);
        setup.Google.Latency = TimeSpan.Zero;
        var change = NewBooking("Field", 8, 10);
        change.Id = booking.Id;
        change.Conduct = "Renamed";
        await setup.Service.UpdateAsync(change, BookerPhone, checkForClash: false);
        await setup.DispatchAsync();
        await slow.WaitAsync(TimeSpan.FromSeconds(30));

        // The slow one has landed on top of it by now, so the calendar is out of date, and the slow one knows
        setup.MakeAllDue();
        await setup.DispatchAsync();

        var @event = await Assert.That(setup.Events()).HasSingleItem();
        await Assert.That(@event["summary"]!.GetValue<string>()).IsEqualTo("Alpha Renamed");
        await Assert.That(setup.State(booking.Id)!.SyncedRevision).IsEqualTo(2);
        await Assert.That(setup.Messages().Select(m => m.Status).Distinct()).IsEquivalentTo([OutboxStatus.Done]);
    }

    [Test]
    public async Task Cancelling_removes_an_event_that_was_there_before_it_was_ever_sent()
    {
        // From before bookings were in the database, so there is no record of sending it
        var setup = await SetUpAsync(connected: false);
        setup.Connect(FakeGoogle.CarbonCopyCalendar);
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        setup.Google.AddBooking(new Booking { Id = booking.Id, Conduct = "Old title", FacilityName = "Field", StartDateTime = At(8), EndDateTime = At(10), UserPhone = BookerPhone });
        await setup.Service.DeleteAsync(booking.Id, BookerPhone);

        await setup.DispatchAsync();

        await Assert.That(setup.Events(FakeGoogle.CarbonCopyCalendar)).IsEmpty();
    }

    [Test]
    public async Task Nothing_is_sent_for_a_tenant_with_no_calendar()
    {
        var setup = await SetUpAsync(connected: false);

        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);
        await setup.DispatchAsync();

        await Assert.That(setup.Messages()).IsEmpty();
        await Assert.That(setup.Google.Requests).IsEmpty();
    }

    [Test]
    public async Task A_message_for_a_calendar_that_was_turned_off_does_nothing()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        setup.Tenant.Db.Updateable<CalendarConnection>().SetColumns(c => new CalendarConnection { Status = CalendarConnectionStatus.Disabled }).Where(c => c.TenantId == setup.Tenant.TenantId).ExecuteCommand();

        await setup.DispatchAsync();

        await Assert.That(setup.Google.Requests).IsEmpty();
        await Assert.That(setup.Messages().Single().Status).IsEqualTo(OutboxStatus.Done);
    }

    private static async Task<int> PlanAsync(Setup setup, bool all = false) =>
        await CalendarSyncPlanner.EnqueueOutOfDateAsync(await setup.Tenant.NewClientAsync(), setup.Signal, setup.Tenant.TenantId, all);

    [Test]
    public async Task Bookings_made_before_the_calendar_was_connected_are_all_sent_when_it_is()
    {
        var setup = await SetUpAsync(connected: false);
        var bookings = new[] { NewBooking("Field", 8, 10, 10), NewBooking("Eiger", 8, 10, 11), NewBooking("Field", 8, 10, 12) };
        await setup.Service.CreateAsync(bookings);
        await Assert.That(setup.Messages()).IsEmpty();

        setup.Connect();
        var found = await PlanAsync(setup);
        await setup.DispatchAsync();

        await Assert.That(found).IsEqualTo(3);
        await Assert.That(setup.Events().Select(e => e["id"]!.GetValue<string>())).IsEquivalentTo(bookings.Select(b => b.Id.ToString("N")).ToList());
        await Assert.That(await PlanAsync(setup)).IsEqualTo(0);
    }

    [Test]
    public async Task A_booking_that_changed_while_the_calendar_was_out_of_reach_is_found()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        await setup.DispatchAsync();
        setup.Google.FailingCalendars.Add(CalendarId);
        var change = NewBooking("Field", 8, 10);
        change.Id = booking.Id;
        change.Conduct = "Renamed";
        await setup.Service.UpdateAsync(change, BookerPhone, checkForClash: false);
        // Given up on, as if it had been out of reach for a long time
        setup.Tenant.Db.Updateable<OutboxMessage>().SetColumns(m => new OutboxMessage { Status = OutboxStatus.Dead }).Where(m => m.TenantId == setup.Tenant.TenantId && m.Status == OutboxStatus.Pending).ExecuteCommand();
        setup.Google.FailingCalendars.Clear();

        var found = await PlanAsync(setup);
        await setup.DispatchAsync();

        await Assert.That(found).IsEqualTo(1);
        await Assert.That(setup.Events().Single()["summary"]!.GetValue<string>()).IsEqualTo("Alpha Renamed");
    }

    [Test]
    public async Task Asking_for_everything_puts_back_what_was_changed_in_the_calendar_by_hand()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        await setup.DispatchAsync();
        setup.Google.RemoveEvent(CalendarId, booking.Id);

        await Assert.That(await PlanAsync(setup)).IsEqualTo(0);
        var found = await PlanAsync(setup, all: true);
        await setup.DispatchAsync();

        await Assert.That(found).IsEqualTo(1);
        await Assert.That(setup.Events()).HasSingleItem();
    }

    [Test]
    public async Task The_event_of_a_booking_cancelled_since_is_found()
    {
        var setup = await SetUpAsync(connected: false);
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        setup.Connect();
        await PlanAsync(setup);
        await setup.DispatchAsync();
        // Cancelled while the calendar was not connected to any more
        setup.Tenant.Db.Updateable<CalendarConnection>().SetColumns(c => new CalendarConnection { Status = CalendarConnectionStatus.Disabled }).Where(c => c.TenantId == setup.Tenant.TenantId).ExecuteCommand();
        await setup.Service.DeleteAsync(booking.Id, BookerPhone);
        setup.Tenant.Db.Updateable<CalendarConnection>().SetColumns(c => new CalendarConnection { Status = CalendarConnectionStatus.Active }).Where(c => c.TenantId == setup.Tenant.TenantId).ExecuteCommand();

        var found = await PlanAsync(setup);
        await setup.DispatchAsync();

        await Assert.That(found).IsEqualTo(1);
        await Assert.That(setup.Events()).IsEmpty();
    }

    [Test]
    public async Task Bookings_that_ended_long_ago_are_left_alone()
    {
        var setup = await SetUpAsync(connected: false);
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10, days: -30), NewBooking("Field", 8, 10, days: 5)]);
        setup.Connect();

        await Assert.That(await PlanAsync(setup)).IsEqualTo(1);
    }

    [Test]
    public async Task Changing_the_calendar_sends_everything_again()
    {
        var setup = await SetUpAsync();
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);
        await setup.DispatchAsync();
        await Assert.That(await PlanAsync(setup)).IsEqualTo(0);

        setup.Tenant.Db.Updateable<CalendarConnection>().SetColumns(c => new CalendarConnection { CalendarId = "another-calendar" }).Where(c => c.TenantId == setup.Tenant.TenantId).ExecuteCommand();

        await Assert.That(await PlanAsync(setup)).IsEqualTo(1);
    }

    [Test]
    public async Task A_calendar_that_is_not_working_is_left_alone()
    {
        var setup = await SetUpAsync(connected: false);
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);

        await Assert.That(await PlanAsync(setup)).IsEqualTo(0);

        setup.Connect();
        setup.Tenant.Db.Updateable<CalendarConnection>().SetColumns(c => new CalendarConnection { Status = CalendarConnectionStatus.Failed }).Where(c => c.TenantId == setup.Tenant.TenantId).ExecuteCommand();
        await Assert.That(await PlanAsync(setup)).IsEqualTo(0);
    }

    [Test]
    public async Task The_reconciler_finds_what_is_out_of_date_for_the_tenants_it_is_for()
    {
        var setup = await SetUpAsync(connected: false);
        var other = await SetUpAsync(connected: false);
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);
        await other.Service.CreateAsync([NewBooking("Field", 8, 10)]);
        setup.Connect();
        other.Connect();
        var reconciler = new CalendarReconciler(
            NullLogger<CalendarReconciler>.Instance,
            await setup.Tenant.NewClientAsync(),
            setup.Signal,
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions { TenantId = setup.Tenant.TenantId }),
            Microsoft.Extensions.Options.Options.Create(new CalendarSyncOptions())
        );

        var found = await reconciler.RunOnceAsync();

        await Assert.That(found).IsEqualTo(1);
        await Assert.That(setup.Messages().Count).IsEqualTo(1);
        await Assert.That(other.Messages()).IsEmpty();
    }
}
