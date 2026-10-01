using Fbs.WebApi.Bookings;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Notifications;
using Fbs.WebApi.Outbox;
using SqlSugar;
using Booking = Fbs.WebApi.Entities.Booking;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// The booking rules against TiDB, without the API in front: that a facility is never booked twice, even by
/// requests at the same moment, and that a batch is made whole or not at all.
/// </summary>
public class DatabaseBookingServiceTests
{
    private const string BookerPhone = "6591000001";
    private const string ColleaguePhone = "6591000002";
    private const string LeaverPhone = "6591000003";

    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    /// <summary>Hours after midnight, Singapore time, ten days from now.</summary>
    private static DateTimeOffset At(double hours, int days = 10)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(days).AddHours(hours);
    }

    private static Booking NewBooking(string facility, double from, double to, string phone = BookerPhone, int days = 10) =>
        new()
        {
            FacilityName = facility,
            StartDateTime = At(from, days),
            EndDateTime = At(to, days),
            Conduct = "Section training",
            Description = "Bring water",
            PocName = "CPT Booker",
            PocPhone = "6598765432",
            UserPhone = phone,
        };

    private sealed record Setup(TestTenant Tenant, DatabaseBookingService Service, Guid Booker, Guid Colleague, Guid Leaver, Guid Alpha)
    {
        public int StoredCount
        {
            get
            {
                var tenantId = Tenant.TenantId;
                return Tenant.Db.Queryable<DataBooking>().Where(b => b.TenantId == tenantId).Count();
            }
        }

        public DataBooking Row(Guid id) => Tenant.Db.Queryable<DataBooking>().Single(b => b.Id == id);

        /// <summary>The messages written to tell people, oldest first.</summary>
        public List<(OutboxMessage Message, TelegramBookingPayload Payload)> Outbox()
        {
            var tenantId = Tenant.TenantId;
            return Tenant
                .Db.Queryable<OutboxMessage>()
                .Where(m => m.TenantId == tenantId)
                .OrderBy(m => m.CreatedAt)
                .ToList()
                .Select(m => (m, JsonSerializer.Deserialize<TelegramBookingPayload>(m.Payload)!))
                .ToList();
        }
    }

    private static async Task<Setup> SetUpAsync(string timeZone = "Asia/Singapore")
    {
        var tenant = await TestTenant.CreateAsync(timeZone);
        var alpha = tenant.AddUnit("Alpha");
        var bravo = tenant.AddUnit("Bravo");
        var booker = tenant.AddMember("CPT Booker", "+" + BookerPhone, alpha);
        var colleague = tenant.AddMember("LTA Colleague", "+" + ColleaguePhone, bravo);
        var leaver = tenant.AddMember("PTE Leaver", "+" + LeaverPhone, alpha, status: MemberStatus.Removed);
        tenant.AddFacility("Eiger", all: true);
        tenant.AddFacility("Field", all: true);
        tenant.AddFacility("Gym", all: true);

        return new Setup(tenant, tenant.BookingServiceFor(await tenant.NewClientAsync()), booker, colleague, leaver, alpha);
    }

    [Test]
    public async Task A_booking_is_stored_with_who_made_it_their_unit_and_the_point_of_contact()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);

        var result = await setup.Service.CreateAsync([booking]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(booking.Id).IsNotEqualTo(Guid.Empty);
        var row = setup.Row(booking.Id);
        await Assert.That(row.BookedByMemberId).IsEqualTo(setup.Booker);
        await Assert.That(row.UnitId).IsEqualTo(setup.Alpha);
        await Assert.That(row.PocName).IsEqualTo("CPT Booker");
        await Assert.That(row.PocPhone).IsEqualTo("6598765432");
        await Assert.That(row.Revision).IsEqualTo(1);
        await Assert.That(row.BatchId).IsNull();
        await Assert.That(row.StartUtc).IsEqualTo(At(8));
    }

    [Test]
    public async Task Bookings_are_listed_the_way_the_api_shows_them_in_the_tenants_time_zone()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);

        var listed = await Assert.That(await setup.Service.ListAsync()).HasSingleItem();

        await Assert.That(listed.Id).IsEqualTo(booking.Id);
        await Assert.That(listed.FacilityName).IsEqualTo("Field");
        await Assert.That(listed.UserPhone).IsEqualTo(BookerPhone);
        await Assert.That(listed.StartDateTime).IsEqualTo(At(8));
        await Assert.That(listed.StartDateTime!.Value.Offset).IsEqualTo(Singapore);
    }

    [Test]
    public async Task A_time_zone_that_is_not_known_shows_times_in_utc()
    {
        var setup = await SetUpAsync(timeZone: "Not/AZone");
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);

        var listed = await Assert.That(await setup.Service.ListAsync()).HasSingleItem();

        await Assert.That(listed.StartDateTime!.Value.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(listed.StartDateTime).IsEqualTo(At(8));
    }

    [Test]
    public async Task Bookings_made_together_share_a_batch()
    {
        var setup = await SetUpAsync();
        var bookings = new[] { NewBooking("Field", 8, 10, days: 10), NewBooking("Field", 8, 10, days: 11), NewBooking("Gym", 8, 10, days: 12) };

        await setup.Service.CreateAsync(bookings);

        var batches = bookings.Select(b => setup.Row(b.Id).BatchId).Distinct().ToList();
        await Assert.That(batches).HasSingleItem();
        await Assert.That(batches[0]).IsNotNull();
    }

    [Test]
    public async Task A_clash_with_a_booking_that_is_there_is_reported_and_nothing_is_stored()
    {
        var setup = await SetUpAsync();
        var existing = NewBooking("Field", 9, 11);
        await setup.Service.CreateAsync([existing]);

        var result = await setup.Service.CreateAsync([NewBooking("Gym", 8, 10), NewBooking("Field", 10, 12)]);

        await Assert.That(result.Conflicts).HasSingleItem();
        await Assert.That(result.Conflicts[0]).IsEqualTo(new BookingConflict(1, existing.Id, null));
        await Assert.That(setup.StoredCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_clash_within_the_batch_is_reported_against_the_earlier_slot()
    {
        var setup = await SetUpAsync();

        var result = await setup.Service.CreateAsync([NewBooking("Field", 8, 10), NewBooking("Field", 9, 11)]);

        await Assert.That(result.Conflicts).HasSingleItem();
        await Assert.That(result.Conflicts[0]).IsEqualTo(new BookingConflict(1, null, 0));
        await Assert.That(setup.StoredCount).IsEqualTo(0);
    }

    [Test]
    public async Task Bookings_that_only_touch_do_not_clash()
    {
        var setup = await SetUpAsync();
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);

        var result = await setup.Service.CreateAsync([NewBooking("Field", 10, 12), NewBooking("Field", 6, 8)]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(setup.StoredCount).IsEqualTo(3);
    }

    [Test]
    public async Task Another_tenants_bookings_of_a_facility_with_the_same_name_do_not_clash_or_show()
    {
        var setup = await SetUpAsync();
        var other = await SetUpAsync();
        await other.Service.CreateAsync([NewBooking("Field", 8, 10)]);

        var result = await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(await setup.Service.ListAsync()).HasSingleItem();
    }

    [Test]
    public async Task A_cancelled_booking_is_kept_but_no_longer_listed_or_in_the_way()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);

        await setup.Service.DeleteAsync(booking.Id, ColleaguePhone);

        var row = setup.Row(booking.Id);
        await Assert.That(row.CancelledAt).IsNotNull();
        await Assert.That(row.CancelledByMemberId).IsEqualTo(setup.Colleague);
        await Assert.That(row.BookedByMemberId).IsEqualTo(setup.Booker);
        await Assert.That(row.Revision).IsEqualTo(2);
        await Assert.That(await setup.Service.ListAsync()).IsEmpty();
        await Assert.That(await setup.Service.FindAsync(booking.Id)).IsNull();
        await Assert.That((await setup.Service.CreateAsync([NewBooking("Field", 8, 10)])).Succeeded).IsTrue();
    }

    [Test]
    public async Task Cancelling_a_booking_twice_changes_nothing_the_second_time()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        await setup.Service.DeleteAsync(booking.Id, ColleaguePhone);
        var first = setup.Row(booking.Id);

        await setup.Service.DeleteAsync(booking.Id, BookerPhone);

        var second = setup.Row(booking.Id);
        await Assert.That(second.CancelledByMemberId).IsEqualTo(setup.Colleague);
        await Assert.That(second.Revision).IsEqualTo(first.Revision);
    }

    [Test]
    public async Task Updating_a_booking_keeps_who_made_it_and_records_who_changed_it()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        var changes = NewBooking("Field", 8, 10);
        changes.Id = booking.Id;
        changes.Conduct = "Renamed";
        changes.PocName = "LTA Colleague";
        changes.UserPhone = ColleaguePhone;

        var result = await setup.Service.UpdateAsync(changes, ColleaguePhone, checkForClash: false);

        await Assert.That(result.Updated).IsNotNull();
        await Assert.That(result.Updated!.UserPhone).IsEqualTo(BookerPhone);
        var row = setup.Row(booking.Id);
        await Assert.That(row.BookedByMemberId).IsEqualTo(setup.Booker);
        await Assert.That(row.UnitId).IsEqualTo(setup.Alpha);
        await Assert.That(row.UpdatedByMemberId).IsEqualTo(setup.Colleague);
        await Assert.That(row.Conduct).IsEqualTo("Renamed");
        await Assert.That(row.PocName).IsEqualTo("LTA Colleague");
        await Assert.That(row.Revision).IsEqualTo(2);
        await Assert.That(row.CreatedAt).IsLessThanOrEqualTo(row.UpdatedAt);
    }

    [Test]
    public async Task A_booking_cannot_move_onto_another()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        var other = NewBooking("Field", 12, 14);
        await setup.Service.CreateAsync([booking, other]);
        var move = NewBooking("Field", 9, 13);
        move.Id = booking.Id;

        var result = await setup.Service.UpdateAsync(move, BookerPhone, checkForClash: true);

        await Assert.That(result.Updated).IsNull();
        await Assert.That(result.ClashesWith).IsEqualTo(other.Id);
        await Assert.That(setup.Row(booking.Id).StartUtc).IsEqualTo(At(8));
        await Assert.That(setup.Row(booking.Id).Revision).IsEqualTo(1);
    }

    [Test]
    public async Task A_booking_can_move_within_its_own_slot()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        var move = NewBooking("Field", 9, 11);
        move.Id = booking.Id;

        var result = await setup.Service.UpdateAsync(move, BookerPhone, checkForClash: true);

        await Assert.That(result.Updated).IsNotNull();
        await Assert.That(setup.Row(booking.Id).StartUtc).IsEqualTo(At(9));
    }

    [Test]
    public async Task A_move_is_checked_for_clashes_even_when_the_caller_said_it_was_not()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        var other = NewBooking("Field", 12, 14);
        await setup.Service.CreateAsync([booking, other]);
        var move = NewBooking("Field", 12, 14);
        move.Id = booking.Id;

        var result = await setup.Service.UpdateAsync(move, BookerPhone, checkForClash: false);

        await Assert.That(result.Updated).IsNull();
        await Assert.That(result.ClashesWith).IsEqualTo(other.Id);
    }

    [Test]
    public async Task Updating_a_cancelled_or_missing_booking_fails()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        await setup.Service.DeleteAsync(booking.Id, BookerPhone);
        booking.Conduct = "Too late";

        await Assert.That(async () => await setup.Service.UpdateAsync(booking, BookerPhone, checkForClash: false)).Throws<KeyNotFoundException>();
        booking.Id = Guid.NewGuid();
        await Assert.That(async () => await setup.Service.UpdateAsync(booking, BookerPhone, checkForClash: false)).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task A_booking_by_someone_who_has_left_is_still_listed_with_their_number()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        var leaver = setup.Leaver;
        var id = booking.Id;
        setup.Tenant.Db.Updateable<DataBooking>().SetColumns(b => new DataBooking { BookedByMemberId = leaver }).Where(b => b.Id == id).ExecuteCommand();

        var listed = await Assert.That(await setup.Service.ListAsync()).HasSingleItem();

        await Assert.That(listed.UserPhone).IsEqualTo(LeaverPhone);
    }

    [Test]
    public async Task What_a_booking_holds_is_limited_to_what_the_columns_do()
    {
        var setup = await SetUpAsync();

        await Assert.That(setup.Service.CanStore(NewBooking("Field", 8, 10))).IsTrue();
        await Assert.That(setup.Service.CanStore(new Booking { Conduct = new string('a', 201) })).IsFalse();
        await Assert.That(setup.Service.CanStore(new Booking { Description = new string('a', 2001) })).IsFalse();
        await Assert.That(setup.Service.CanStore(new Booking { Description = new string('a', 2000) })).IsTrue();
        await Assert.That(setup.Service.CanStore(new Booking { PocName = new string('a', 201) })).IsFalse();
        await Assert.That(setup.Service.CanStore(new Booking { PocPhone = new string('1', 33) })).IsFalse();
    }

    [Test]
    public async Task A_batch_that_fails_part_way_stores_none_and_lets_go_of_the_facility()
    {
        var setup = await SetUpAsync();
        var tooLong = NewBooking("Field", 12, 14);
        tooLong.Conduct = new string('a', 500);

        // The database refuses the third, after the first two were written
        await Assert
            .That(async () => await setup.Service.CreateAsync([NewBooking("Field", 8, 10), NewBooking("Field", 10, 12), tooLong]))
            .Throws<Exception>();

        await Assert.That(setup.StoredCount).IsEqualTo(0);
        // Another request for the facility isn't left waiting for a lock the failure still holds
        var next = await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]).WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(next.Succeeded).IsTrue();
    }

    [Test]
    public async Task A_booking_writes_a_message_to_tell_people_about_it()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);

        await setup.Service.CreateAsync([booking]);

        var (message, payload) = await Assert.That(setup.Outbox()).HasSingleItem();
        await Assert.That(message.Type).IsEqualTo(TelegramBookingNotifier.MessageType);
        await Assert.That(message.Status).IsEqualTo(OutboxStatus.Pending);
        await Assert.That(payload.Change).IsEqualTo(BookingChange.Created);
        await Assert.That(payload.BookingIds).IsEquivalentTo([booking.Id]);
        await Assert.That(payload.ActorMemberId).IsEqualTo(setup.Booker);
    }

    [Test]
    public async Task Bookings_made_together_write_one_message_between_them()
    {
        var setup = await SetUpAsync();
        var bookings = new[] { NewBooking("Field", 8, 10, days: 10), NewBooking("Field", 8, 10, days: 11), NewBooking("Gym", 8, 10, days: 12) };

        await setup.Service.CreateAsync(bookings);

        var (_, payload) = await Assert.That(setup.Outbox()).HasSingleItem();
        await Assert.That(payload.BookingIds).IsEquivalentTo(bookings.Select(b => b.Id).ToList());
    }

    [Test]
    public async Task Nobody_is_told_about_a_booking_that_was_not_made()
    {
        var setup = await SetUpAsync();
        await setup.Service.CreateAsync([NewBooking("Field", 8, 10)]);
        var tooLong = NewBooking("Field", 14, 16);
        tooLong.Conduct = new string('a', 500);

        await setup.Service.CreateAsync([NewBooking("Field", 9, 11)]);
        await Assert.That(async () => await setup.Service.CreateAsync([NewBooking("Field", 12, 13), tooLong])).Throws<Exception>();

        // Only the first, which was made
        await Assert.That(setup.Outbox()).HasSingleItem();
    }

    [Test]
    public async Task An_update_writes_a_message_that_says_where_it_was_only_when_it_moved()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);
        var rename = NewBooking("Field", 8, 10);
        rename.Id = booking.Id;
        rename.Conduct = "Renamed";
        var move = NewBooking("Field", 12, 14);
        move.Id = booking.Id;

        await setup.Service.UpdateAsync(rename, ColleaguePhone, checkForClash: false);
        await setup.Service.UpdateAsync(move, ColleaguePhone, checkForClash: true);

        var outbox = setup.Outbox();
        await Assert.That(outbox.Count).IsEqualTo(3);
        await Assert.That(outbox[1].Payload.Change).IsEqualTo(BookingChange.Updated);
        await Assert.That(outbox[1].Payload.ActorMemberId).IsEqualTo(setup.Colleague);
        await Assert.That(outbox[1].Payload.PreviousStartUtc).IsNull();
        await Assert.That(outbox[2].Payload.PreviousStartUtc).IsEqualTo(At(8));
        await Assert.That(outbox[2].Payload.PreviousEndUtc).IsEqualTo(At(10));
    }

    [Test]
    public async Task A_clash_on_update_writes_nothing()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking, NewBooking("Field", 12, 14)]);
        var move = NewBooking("Field", 12, 14);
        move.Id = booking.Id;

        await setup.Service.UpdateAsync(move, BookerPhone, checkForClash: true);

        await Assert.That(setup.Outbox().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Cancelling_writes_a_message_once()
    {
        var setup = await SetUpAsync();
        var booking = NewBooking("Field", 8, 10);
        await setup.Service.CreateAsync([booking]);

        await setup.Service.DeleteAsync(booking.Id, ColleaguePhone);
        await setup.Service.DeleteAsync(booking.Id, ColleaguePhone);

        var outbox = setup.Outbox();
        await Assert.That(outbox.Count).IsEqualTo(2);
        await Assert.That(outbox[1].Payload.Change).IsEqualTo(BookingChange.Cancelled);
        await Assert.That(outbox[1].Payload.ActorMemberId).IsEqualTo(setup.Colleague);
        await Assert.That(outbox[1].Payload.BookingIds).IsEquivalentTo([booking.Id]);
    }

    [Test]
    public async Task The_dispatcher_is_woken_when_a_booking_is_saved_and_not_when_it_is_not()
    {
        var setup = await SetUpAsync();
        var signal = new OutboxSignal();
        var service = setup.Tenant.BookingServiceFor(await setup.Tenant.NewClientAsync(), signal);
        var woken = async () =>
        {
            var started = DateTime.UtcNow;
            await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            return DateTime.UtcNow - started < TimeSpan.FromMilliseconds(500);
        };

        await service.CreateAsync([NewBooking("Field", 8, 10)]);
        await Assert.That(await woken()).IsTrue();

        var clash = await service.CreateAsync([NewBooking("Field", 9, 11)]);
        await Assert.That(clash.Succeeded).IsFalse();
        await Assert.That(await woken()).IsFalse();
    }

    [Test]
    public async Task Requests_at_the_same_moment_for_one_slot_book_it_once()
    {
        var setup = await SetUpAsync();
        var client = await setup.Tenant.NewClientAsync();

        for (var round = 0; round < 5; round++)
        {
            var day = 20 + round;
            var results = await RunTogetherAsync(
                8,
                i => setup.Tenant.BookingServiceFor(client).CreateAsync([NewBooking("Field", 8, 10, i % 2 == 0 ? BookerPhone : ColleaguePhone, day)])
            );

            await Assert.That(results.Count(r => r.Succeeded)).IsEqualTo(1);
            var tenantId = setup.Tenant.TenantId;
            var start = At(8, day);
            await Assert.That(setup.Tenant.Db.Queryable<DataBooking>().Count(b => b.TenantId == tenantId && b.StartUtc == start)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Requests_at_the_same_moment_for_overlapping_slots_never_leave_two_that_overlap()
    {
        var setup = await SetUpAsync();
        var client = await setup.Tenant.NewClientAsync();

        // Each starts half an hour after the last and lasts two hours, so neighbours clash, and every other does
        await RunTogetherAsync(
            10,
            i => setup.Tenant.BookingServiceFor(client).CreateAsync([NewBooking("Field", 6 + i * 0.5, 8 + i * 0.5)])
        );

        var stored = await setup.Service.ListAsync();
        await Assert.That(stored).IsNotEmpty();
        for (var i = 0; i < stored.Count; i++)
        {
            for (var j = i + 1; j < stored.Count; j++)
            {
                await Assert.That(BookingOverlaps.Overlap(stored[i], stored[j])).IsFalse();
            }
        }
    }

    [Test]
    public async Task Batches_over_the_same_facilities_at_the_same_moment_all_finish()
    {
        var setup = await SetUpAsync();
        var client = await setup.Tenant.NewClientAsync();

        // Some ask for the facilities one way round and some the other, which would deadlock if they were
        // locked in the order asked for rather than in an order that is the same for everyone
        var results = await RunTogetherAsync(
            8,
            i =>
            {
                var (first, second) = i % 2 == 0 ? ("Eiger", "Gym") : ("Gym", "Eiger");
                return setup.Tenant.BookingServiceFor(client)
                    .CreateAsync([NewBooking(first, 8, 10, days: 30 + i), NewBooking(second, 8, 10, days: 30 + i), NewBooking(first, 12, 14, days: 30 + i)]);
            }
        );

        await Assert.That(results.All(r => r.Succeeded)).IsTrue();
        await Assert.That(setup.StoredCount).IsEqualTo(24);
    }

    [Test]
    public async Task Moving_bookings_and_making_them_at_the_same_moment_never_leaves_two_that_overlap()
    {
        var setup = await SetUpAsync();
        var client = await setup.Tenant.NewClientAsync();
        var existing = Enumerable.Range(0, 4).Select(i => NewBooking("Field", 6 + i * 4, 8 + i * 4)).ToList();
        await setup.Service.CreateAsync(existing);

        // Four move to the same slot and four more ask for it. Whichever wins, only one has it
        await RunTogetherAsync(
            8,
            async i =>
            {
                var service = setup.Tenant.BookingServiceFor(client);
                if (i < 4)
                {
                    var move = NewBooking("Field", 30, 32);
                    move.Id = existing[i].Id;
                    var moved = await service.UpdateAsync(move, BookerPhone, checkForClash: true);
                    return new CreateResult(moved.Updated is null ? [new BookingConflict(0, moved.ClashesWith, null)] : []);
                }

                return await service.CreateAsync([NewBooking("Field", 30, 32, ColleaguePhone)]);
            }
        );

        var stored = await setup.Service.ListAsync();
        await Assert.That(stored.Count(b => b.StartDateTime == At(30))).IsEqualTo(1);
        for (var i = 0; i < stored.Count; i++)
        {
            for (var j = i + 1; j < stored.Count; j++)
            {
                await Assert.That(BookingOverlaps.Overlap(stored[i], stored[j])).IsFalse();
            }
        }
    }

    /// <summary>
    /// Runs the calls together, each as a request of its own would, with nothing carried over from the
    /// test, then waits for them. Fails if they don't all finish, which is what a deadlock looks like.
    /// </summary>
    private static async Task<List<CreateResult>> RunTogetherAsync(int count, Func<int, Task<CreateResult>> call)
    {
        var start = new TaskCompletionSource();
        List<Task<CreateResult>> tasks;
        using (ExecutionContext.SuppressFlow())
        {
            tasks = Enumerable
                .Range(0, count)
                .Select(i =>
                    Task.Run(async () =>
                    {
                        await start.Task;
                        return await call(i);
                    })
                )
                .ToList();
        }

        start.SetResult();
        return [.. await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60))];
    }
}
