using Fbs.WebApi.Tests.Data;

namespace Fbs.WebApi.Tests.Helpers;

public static class StoredBookingAssertions
{
    /// <summary>
    /// That everywhere bookings are kept holds this many, so a booking that is meant to be gone, or to
    /// be there, is in each of them.
    /// </summary>
    public static async Task AssertStoredBookingsAsync(this FbsApiFactory factory, int expected)
    {
        var counts = factory.StoredBookingCounts;
        await Assert.That(counts).IsEquivalentTo(Enumerable.Repeat(expected, counts.Count).ToList());
    }
}
