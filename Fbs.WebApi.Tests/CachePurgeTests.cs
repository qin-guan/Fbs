using System.Net;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Fakes;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

public abstract class CachePurgeTests(FbsApiFactory factory)
{
    protected FbsApiFactory Factory { get; } = factory;

    private int FullCalendarLists() =>
        Factory.Google.Requests.Count(r => r.IsFullEventList(FakeGoogle.MainCalendar));

    [Test]
    public async Task Anonymous_requests_cannot_purge_the_cache()
    {
        using var client = Factory.CreateClient();
        await client.GetAsync("/Booking"); // Loads the bookings once, so a reload would show
        var before = FullCalendarLists();

        var response = await client.GetAsync("/Cache/Purge");

        await Assert.That(response).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(FullCalendarLists()).IsEqualTo(before);
    }

    [Test]
    public async Task Users_who_are_not_admins_cannot_purge_the_cache()
    {
        using var client = Factory.CreateClientFor(Users.Booker);
        (await client.GetAsync("/Booking")).EnsureSuccessStatusCode();
        var before = FullCalendarLists();

        var response = await client.GetAsync("/Cache/Purge");

        await Assert.That(response).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(FullCalendarLists()).IsEqualTo(before);
    }

    [Test]
    public async Task Admins_can_purge_the_cache()
    {
        using var client = Factory.CreateClientFor(Users.Admin);
        (await client.GetAsync("/Booking")).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/Cache/Purge");

        response.EnsureSuccessStatusCode();
    }
}

/// <summary>CachePurgeTests with users, facilities, the roster and login codes in Google Sheets.</summary>
[ClassDataSource<FbsApiFactory>]
[InheritsTests]
public class GoogleCachePurgeTests(FbsApiFactory factory) : CachePurgeTests(factory);

/// <summary>CachePurgeTests with users, facilities, the roster and login codes in the database.</summary>
[ClassDataSource<DatabaseFbsApiFactory>]
[InheritsTests]
public class DatabaseCachePurgeTests(DatabaseFbsApiFactory factory) : CachePurgeTests(factory);
