using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Entities;
using Fbs.WebApi.Tests.Fakes;
using Xunit.Abstractions;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Checks every operation stays under a second with years of bookings and realistic round trips to
/// Google and Telegram.
/// </summary>
public class PerformanceTests : IDisposable
{
    private const int ExistingBookings = 5000;
    private const int Subscribers = 20;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Singapore = TimeSpan.FromHours(8);

    private readonly ITestOutputHelper _output;
    private readonly FbsApiFactory _factory = new();
    private readonly List<(string Operation, TimeSpan Elapsed)> _timings = [];

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;

        var facilities = new[] { "Eiger", "Field", "Gym" };
        var users = new[] { Users.Booker, Users.SameUnit, Users.AllGroup, Users.OtherUnit };
        for (var i = 0; i < ExistingBookings; i++)
        {
            var start = Midnight(-1 - i / 3).AddHours(8 + i % 3 * 3);
            _factory.Google.AddBooking(
                new Booking
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
                }
            );
        }

        for (var i = 0; i < Subscribers; i++)
        {
            _factory.Google.Sheets["Users"].Add(
                ["Delta", $"Subscriber {i}", $"6570000{i:000}", $"{2000 + i}", "All", "FALSE"]
            );
        }

        _factory.Google.Latency = TimeSpan.FromMilliseconds(150);
        _factory.Telegram.Latency = TimeSpan.FromMilliseconds(100);
    }

    public void Dispose()
    {
        _factory.Dispose();
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
        Assert.True(
            response.IsSuccessStatusCode,
            $"{operation} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
        return response;
    }

    [Fact]
    public async Task Every_operation_takes_under_a_second_with_thousands_of_bookings()
    {
        using var client = _factory.CreateClientFor(Users.Booker);

        await TimeAsync("List bookings (first request)", () => client.GetAsync("/Booking"));
        var list = await TimeAsync("List bookings", () => client.GetAsync("/Booking"));
        Assert.Equal(ExistingBookings, (await list.Content.ReadFromJsonAsync<List<JsonElement>>())!.Count);

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
        Assert.Equal(
            ExistingBookings + 8,
            (await afterBatch.Content.ReadFromJsonAsync<List<JsonElement>>())!.Count
        );

        await TimeAsync("List facilities", () => client.GetAsync("/Facility"));
        await TimeAsync("Get signed in user", () => client.GetAsync("/Auth/Me"));

        foreach (var (operation, elapsed) in _timings)
        {
            _output.WriteLine($"{operation,-32} {elapsed.TotalMilliseconds,7:0} ms");
        }

        Assert.All(_timings, t => Assert.True(t.Elapsed < Budget, $"{t.Operation} took {t.Elapsed.TotalMilliseconds:0} ms"));
    }
}
