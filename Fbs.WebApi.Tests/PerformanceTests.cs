using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Data;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Checks every operation stays under a second with years of bookings and realistic round trips to
/// Google and Telegram.
/// </summary>
public abstract class PerformanceTests(FbsApiFactory factory)
{
    private const int ExistingBookings = 5000;
    private const int Subscribers = 20;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    protected FbsApiFactory Factory { get; } = factory;

    private readonly List<(string Operation, TimeSpan Elapsed)> _timings = [];

    [Before(Test)]
    public void AddBookingsAndSubscribers()
    {
        var facilities = new[] { "Eiger", "Field", "Gym" };
        var users = new[] { Users.Booker, Users.SameUnit, Users.AllGroup, Users.OtherUnit };
        Factory.AddBookings(
            Enumerable
                .Range(0, ExistingBookings)
                .Select(i =>
                {
                    var start = Midnight(-1 - i / 3).AddHours(8 + i % 3 * 3);
                    return new Booking
                    {
                        Id = Guid.NewGuid(),
                        FacilityName = facilities[i % facilities.Length],
                        Conduct = $"Conduct {i}",
                        Description = "Past training",
                        PocName = "POC",
                        PocPhone = "6590000000",
                        StartDateTime = start,
                        EndDateTime = start.AddHours(2),
                        UserPhone = users[i % users.Length],
                    };
                })
                .ToList()
        );

        for (var i = 0; i < Subscribers; i++)
        {
            Factory.AddUser("Delta", $"Subscriber {i}", $"6570000{i:000}", $"{2000 + i}", "All");
        }

        Factory.Google.Latency = TimeSpan.FromMilliseconds(150);
        Factory.Telegram.Latency = TimeSpan.FromMilliseconds(100);
    }

    private static DateTimeOffset Midnight(int daysFromNow)
    {
        var today = DateTimeOffset.UtcNow.ToOffset(Singapore);
        return new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, Singapore).AddDays(daysFromNow);
    }

    private async Task<HttpResponseMessage> TimeAsync(string operation, Func<Task<HttpResponseMessage>> send)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await send();
        await response.Content.LoadIntoBufferAsync();
        _timings.Add((operation, stopwatch.Elapsed));
        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail($"{operation} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return response;
    }

    [Test]
    public async Task Every_operation_takes_under_a_second_with_thousands_of_bookings()
    {
        using var client = Factory.CreateClientFor(Users.Booker);

        await TimeAsync("List bookings (first request)", () => client.GetAsync("/Booking"));
        var list = await TimeAsync("List bookings", () => client.GetAsync("/Booking"));
        await Assert.That((await list.Content.ReadFromJsonAsync<List<JsonElement>>())!).Count().IsEqualTo(ExistingBookings);

        var created = await TimeAsync(
            "Create booking",
            () =>
                client.PostAsJsonAsync(
                    "/Booking",
                    new
                    {
                        conduct = "Range",
                        facilityName = "Field",
                        startDateTime = Midnight(10).AddHours(8),
                        endDateTime = Midnight(10).AddHours(10),
                    }
                )
        );
        var id = (await created.Content.ReadFromJsonAsync<Booking>())!.Id;

        await TimeAsync("List bookings after creating", () => client.GetAsync("/Booking"));
        await TimeAsync("Get booking", () => client.GetAsync($"/Booking/{id}"));
        await TimeAsync(
            "Update booking",
            () => client.PostAsJsonAsync($"/Booking/{id}", new { conduct = "Range practice" })
        );
        await TimeAsync("List bookings after updating", () => client.GetAsync("/Booking"));
        await TimeAsync("Delete booking", () => client.DeleteAsync($"/Booking/{id}"));
        await TimeAsync("List bookings after deleting", () => client.GetAsync("/Booking"));

        await TimeAsync(
            "Create batch of 8 bookings",
            () =>
                client.PostAsJsonAsync(
                    "/Booking/Batch",
                    new
                    {
                        conduct = "Field camp",
                        slots = Enumerable
                            .Range(20, 8)
                            .Select(day => new
                            {
                                facilityName = "Eiger",
                                startDateTime = Midnight(day),
                                endDateTime = Midnight(day + 1),
                            })
                            .ToArray(),
                    }
                )
        );
        var afterBatch = await TimeAsync("List bookings after batch", () => client.GetAsync("/Booking"));
        await Assert
            .That((await afterBatch.Content.ReadFromJsonAsync<List<JsonElement>>())!)
            .Count()
            .IsEqualTo(ExistingBookings + 8);

        await TimeAsync("List facilities", () => client.GetAsync("/Facility"));
        await TimeAsync("Get signed in user", () => client.GetAsync("/Auth/Me"));

        foreach (var (operation, elapsed) in _timings)
        {
            TestContext.Current!.Output.WriteLine($"{operation,-32} {elapsed.TotalMilliseconds,7:0} ms");
        }

        var slow = _timings
            .Where(t => t.Elapsed >= Budget)
            .Select(t => $"{t.Operation} took {t.Elapsed.TotalMilliseconds:0} ms");
        await Assert.That(slow).IsEmpty();
    }
}

/// <summary>PerformanceTests with users, facilities, the roster and login codes in Google Sheets.</summary>
[ClassDataSource<FbsApiFactory>]
[InheritsTests]
public class GooglePerformanceTests(FbsApiFactory factory) : PerformanceTests(factory);

/// <summary>PerformanceTests with users, facilities, the roster and login codes in the database.</summary>
[ClassDataSource<DatabaseFbsApiFactory>]
[InheritsTests]
public class DatabasePerformanceTests(DatabaseFbsApiFactory factory) : PerformanceTests(factory);
